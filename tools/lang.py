#!/usr/bin/env python3
"""The texts of AFK Realm's window and their translations.

Every text is written in English in the code, inside L.T("...") or L.F("... {0} ...", value).
The English text is the key in the language files lang/<code>.json.

  python3 tools/lang.py keys        writes lang/template.json: every text of the code (built into the exe,
                                    for "Add a language ..." in AFK Realm)
  python3 tools/lang.py check       for every language: texts missing, texts no longer used, {0} places that differ
  python3 tools/lang.py missing de  prints the texts lang/de.json lacks, as JSON to translate
  python3 tools/lang.py merge de f  takes the translations of the JSON file f into lang/de.json
  python3 tools/lang.py prune       takes texts the code no longer uses out of every language file
  python3 tools/lang.py todo        lists text in the code not inside L.T / L.F that looks like words for people

Module folders carry their own lang/<code>.json for the texts of their afk-realm.json and options; the same
commands work there with --module <folder> (keys are then read from afk-realm.json, races.json,\ndata/journey-ui.json and conf/*.conf.dist; the options are read with AFK Realm's own code, which needs mcs and mono).
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANGS = ["de", "fr", "es", "pt-BR", "ru", "zh-Hans"]


def literal_at(src, i):
    """Reads the C# string literal starting at src[i]; returns (text, end) or None."""
    verbatim = False
    if src[i] == '@' and i + 1 < len(src) and src[i + 1] == '"':
        verbatim = True
        i += 1
    if src[i] != '"':
        return None
    i += 1
    out = []
    while i < len(src):
        c = src[i]
        if verbatim:
            if c == '"':
                if i + 1 < len(src) and src[i + 1] == '"':
                    out.append('"'); i += 2; continue
                return ''.join(out), i + 1
            out.append(c); i += 1; continue
        if c == '\\':
            n = src[i + 1]
            simple = {'n': '\n', 'r': '\r', 't': '\t', '"': '"', "'": "'", '\\': '\\', '0': '\0'}
            if n in simple:
                out.append(simple[n]); i += 2; continue
            if n == 'u':
                out.append(chr(int(src[i + 2:i + 6], 16))); i += 6; continue
            if n == 'x':
                m = re.match(r'[0-9a-fA-F]{1,4}', src[i + 2:])
                out.append(chr(int(m.group(0), 16))); i += 2 + len(m.group(0)); continue
            out.append(n); i += 2; continue
        if c == '"':
            return ''.join(out), i + 1
        if c == '\n':
            return None
        out.append(c); i += 1
    return None


def skip_ws(src, i):
    while i < len(src):
        if src[i] in ' \t\r\n':
            i += 1
        elif src.startswith('//', i):
            j = src.find('\n', i)
            i = len(src) if j < 0 else j + 1
        else:
            break
    return i


def joined_literal(src, i):
    """A literal or literals joined by +: ("a " + "b"). Returns (text, end) or None when not only literals."""
    parts = []
    while True:
        i = skip_ws(src, i)
        r = literal_at(src, i)
        if r is None:
            return None
        parts.append(r[0])
        i = skip_ws(src, r[1])
        if i < len(src) and src[i] == '+':
            j = skip_ws(src, i + 1)
            if j < len(src) and (src[j] == '"' or src.startswith('@"', j)):
                i = j
                continue
            return None   # literal + something else: not a pure text
        return ''.join(parts), i


CALL = re.compile(r'\bL\.(T|F|N|Outside)\(')
CTX = re.compile(r'\bL\.C\(\s*"([^"]+)"\s*,')


def code_keys(files):
    keys = {}
    bad = []
    for f in files:
        src = open(f, encoding='utf-8-sig').read()
        if f.endswith('Lang.cs'):
            continue
        for m in CALL.finditer(src):
            r = joined_literal(src, m.end())
            if r is None:
                if m.group(1) != 'Outside' and not re.match(r'\s*[A-Za-z_][\w.\[\]]*\s*[,)]', src[m.end():m.end() + 80]):
                    line = src.count('\n', 0, m.start()) + 1
                    bad.append('%s:%d: L.%s( without a plain text' % (os.path.relpath(f, ROOT), line, m.group(1)))
                continue
            text = r[0].replace('\r\n', '\n').strip()
            if text:
                keys.setdefault(text, '%s:%d' % (os.path.relpath(f, ROOT), src.count('\n', 0, m.start()) + 1))
        for m in CTX.finditer(src):
            r = joined_literal(src, m.end())
            if r and r[0].strip():
                keys.setdefault(m.group(1) + '|' + r[0].strip(), '%s:%d' % (os.path.relpath(f, ROOT), src.count('\n', 0, m.start()) + 1))
    return keys, bad


PS_TR = re.compile(r"(?:\bTr|Enter-Phase\s+'\w+')\s+'((?:[^']|'')*)'")


def engine_keys(keys):
    """Texts of the PowerShell engine marked for translation: Tr 'text' ... and the phase names."""
    path = os.path.join(ROOT, 'engine', 'engine.ps1')
    if not os.path.exists(path):
        return
    src = open(path, encoding='utf-8-sig').read()
    for m in PS_TR.finditer(src):
        text = m.group(1).replace("''", "'").strip()
        if text:
            keys.setdefault(text, 'engine/engine.ps1:%d' % (src.count('\n', 0, m.start()) + 1))


def cs_files():
    out = []
    for d in (os.path.join(ROOT, 'src'), os.path.join(ROOT, 'app', 'Web'), os.path.join(ROOT, 'app', 'Shims')):
        if os.path.isdir(d):
            out += sorted(os.path.join(d, f) for f in os.listdir(d) if f.endswith('.cs'))
    return out


JS_CALL = re.compile(r"(?<![\w.$])([TF])\(\s*")
JS_CTX = re.compile(r"(?<![\w.$])C\(\s*'([^']*)'\s*,\s*")


def js_literal(src, i):
    """Reads the JavaScript string literal ('..' or "..") at src[i]; returns (text, end) or None."""
    if i >= len(src) or src[i] not in "'\"":
        return None
    q = src[i]; i += 1; out = []
    while i < len(src):
        c = src[i]
        if c == '\\':
            n = src[i + 1]
            simple = {'n': '\n', 'r': '\r', 't': '\t', '"': '"', "'": "'", '\\': '\\', '0': '\0'}
            if n in simple:
                out.append(simple[n]); i += 2; continue
            if n == 'u':
                out.append(chr(int(src[i + 2:i + 6], 16))); i += 6; continue
            out.append(n); i += 2; continue
        if c == q:
            return ''.join(out), i + 1
        if c == '\n':
            return None
        out.append(c); i += 1
    return None


def js_keys(keys):
    """Texts of the web page (app/wwwroot/js): T('text'), F('text {0}', ...) and C('context', 'text')."""
    d = os.path.join(ROOT, 'app', 'wwwroot', 'js')
    if not os.path.isdir(d):
        return
    for name in sorted(os.listdir(d)):
        if not name.endswith('.js'):
            continue
        path = os.path.join(d, name)
        src = open(path, encoding='utf-8').read()
        for m in JS_CALL.finditer(src):
            r = js_literal(src, m.end())
            if r and r[0].strip():
                keys.setdefault(r[0].replace('\r\n', '\n').strip(), 'app/wwwroot/js/%s:%d' % (name, src.count('\n', 0, m.start()) + 1))
        for m in JS_CTX.finditer(src):
            r = js_literal(src, m.end())
            if r and r[0].strip():
                keys.setdefault(m.group(1) + '|' + r[0].strip(), 'app/wwwroot/js/%s:%d' % (name, src.count('\n', 0, m.start()) + 1))


def load(path):
    if not os.path.exists(path):
        return {"code": "", "name": "", "english": "", "font": "", "strings": {}}
    return json.load(open(path, encoding='utf-8'))


def save(path, data):
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write('\n')


PLACES = re.compile(r'\{(\d+)(?:[,:][^}]*)?\}')


def places(s):
    return sorted(set(PLACES.findall(s)))


# ---- modules ----

def camel_words(key):
    """HighElf -> High Elf, the way AFK Realm names a race by its key."""
    return re.sub(r'(?<=[a-z])(?=[A-Z])', ' ', key)


def conf_texts(folder):
    """The sections, descriptions and choice names of the module's conf/*.conf.dist, read by AFK Realm's own
    ConfigModel (tools/ConfDump.cs built with mcs and run with mono)."""
    conf = os.path.join(folder, 'conf')
    dists = sorted(os.path.join(conf, f) for f in os.listdir(conf) if f.endswith('.conf.dist')) if os.path.isdir(conf) else []
    if not dists:
        return []
    import subprocess, tempfile
    exe = os.path.join(tempfile.gettempdir(), 'afk-realm-confdump.exe')
    refs = ['-r:System.Windows.Forms.dll', '-r:System.Drawing.dll', '-r:System.Numerics.dll', '-r:System.Core.dll', '-r:System.Security.dll',
            '-r:System.IO.Compression.dll', '-r:System.IO.Compression.FileSystem.dll', '-r:System.Web.Extensions.dll']
    built = subprocess.run(['mcs', '-langversion:5', '-target:exe', '-sdk:4.5', '-warn:0', '-main:CoAInstaller.ConfDump',
                            '-out:' + exe] + refs + cs_files() + [os.path.join(ROOT, 'tools', 'ConfDump.cs')],
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if built.returncode != 0:
        sys.exit(built.stdout.decode('utf-8', 'replace'))
    out = subprocess.run(['mono', exe] + dists, check=True, stdout=subprocess.PIPE).stdout.decode('utf-8')
    return json.loads(out)


def module_keys(folder):
    """The texts a module brings that AFK Realm shows: the names and whys of afk-realm.json, the names of
    races.json and data/journey-ui.json, and the sections, descriptions and choices of its options."""
    keys = {}

    def add(text, where):
        if isinstance(text, str) and text.strip():
            keys.setdefault(text.replace('\r\n', '\n').strip(), where)

    p = os.path.join(folder, 'afk-realm.json')
    if os.path.exists(p):
        m = json.load(open(p, encoding='utf-8-sig'))
        for patch in m.get('patches', []):
            add(patch.get('name'), 'afk-realm.json patches')
            add(patch.get('why'), 'afk-realm.json patches')
        for setting in m.get('settings', []):
            add(setting.get('why'), 'afk-realm.json settings')
        for part in ('client', 'bots'):
            if isinstance(m.get(part), dict):
                add(m[part].get('why'), 'afk-realm.json ' + part)
    p = os.path.join(folder, 'races.json')
    if os.path.exists(p):
        for key, race in json.load(open(p, encoding='utf-8-sig')).get('known', {}).items():
            add(camel_words(key), 'races.json')
            for field in ('about', 'note'):
                add(race.get(field), 'races.json')
            if isinstance(race.get('virtual'), dict):
                add(race['virtual'].get('name'), 'races.json')
    p = os.path.join(folder, 'data', 'journey-ui.json')
    if os.path.exists(p):
        ui = json.load(open(p, encoding='utf-8-sig'))
        for part in ('zones', 'instances'):
            for place in ui.get(part, []):
                add(place.get('name'), 'journey-ui.json')
    for text in conf_texts(folder):
        add(text, 'conf')
    # Texts AFK Realm itself translates ("General") need no second translation.
    own = load(os.path.join(ROOT, 'lang', 'template.json')).get('strings', {})
    return {k: v for k, v in keys.items() if k not in own}


def main():
    args = sys.argv[1:]
    folder = ROOT
    module = False
    if '--module' in args:
        i = args.index('--module')
        folder = os.path.abspath(args[i + 1]); module = True
        del args[i:i + 2]
    lang_dir = os.path.join(folder, 'lang')
    cmd = args[0] if args else 'check'

    if module:
        keys = module_keys(folder)
        bad = []
    else:
        keys, bad = code_keys(cs_files())
        engine_keys(keys)
        js_keys(keys)

    if cmd == 'keys':
        os.makedirs(lang_dir, exist_ok=True)
        save(os.path.join(lang_dir, 'template.json'),
             {"code": "template", "name": "", "english": "", "font": "", "strings": {k: "" for k in keys}})
        print('%d texts' % len(keys))
        for b in bad:
            print(b)
    elif cmd == 'check':
        for b in bad:
            print(b)
        print('%d texts in the code' % len(keys))
        for code in LANGS:
            data = load(os.path.join(lang_dir, code + '.json'))
            s = data.get('strings', {})
            missing = [k for k in keys if not s.get(k)]
            unused = [k for k in s if k not in keys]
            wrong = [k for k in keys if s.get(k) and places(k) != places(s[k])]
            print('%-8s missing %4d  unused %4d  places differ %d' % (code, len(missing), len(unused), len(wrong)))
            for k in wrong:
                print('   places: ' + k[:80].replace('\n', ' '))
    elif cmd == 'missing':
        code = args[1]
        data = load(os.path.join(lang_dir, code + '.json'))
        s = data.get('strings', {})
        limit = int(args[2]) if len(args) > 2 else 0
        out = {k: "" for k in keys if not s.get(k)}
        if limit:
            out = dict(list(out.items())[:limit])
        print(json.dumps(out, ensure_ascii=False, indent=1))
    elif cmd == 'merge':
        code, src = args[1], args[2]
        path = os.path.join(lang_dir, code + '.json')
        data = load(path)
        new = json.load(open(src, encoding='utf-8'))
        if 'strings' in new and isinstance(new['strings'], dict):
            for k in ('code', 'name', 'english', 'font'):
                if new.get(k):
                    data[k] = new[k]
            new = new['strings']
        data.setdefault('strings', {})
        n = 0
        for k, v in new.items():
            k = k.replace('\r\n', '\n').strip()
            if v and k in keys:
                data['strings'][k] = v; n += 1
            elif v:
                print('not in the code: ' + k[:80].replace('\n', ' '))
        # Keep the order of the code.
        data['strings'] = {k: data['strings'][k] for k in keys if k in data['strings']}
        if not data.get('code'):
            data['code'] = code
        save(path, data)
        print('%s: %d taken, %d of %d done' % (code, n, len(data['strings']), len(keys)))
    elif cmd == 'prune':
        for code in LANGS:
            path = os.path.join(lang_dir, code + '.json')
            if not os.path.exists(path):
                continue
            data = load(path)
            data['strings'] = {k: data['strings'][k] for k in keys if k in data['strings']}
            save(path, data)
    elif cmd == 'todo':
        only = args[1] if len(args) > 1 else None
        for f in cs_files():
            if only and only not in f:
                continue
            src = open(f, encoding='utf-8-sig').read()
            covered = []
            for m in CALL.finditer(src):
                r = joined_literal(src, m.end())
                if r:
                    covered.append((m.start(), r[1]))
            i = 0
            while i < len(src):
                if src.startswith('//', i):
                    j = src.find('\n', i); i = len(src) if j < 0 else j; continue
                if src.startswith('/*', i):
                    j = src.find('*/', i); i = len(src) if j < 0 else j + 2; continue
                if src[i] == "'" :
                    j = src.find("'", i + 1 + (1 if src[i + 1] == '\\' else 0)); i = j + 1; continue
                if src[i] == '"' or src.startswith('@"', i):
                    r = literal_at(src, i)
                    if r is None:
                        i += 1; continue
                    text, end = r
                    inside = any(a <= i < b for a, b in covered)
                    if not inside and re.search(r'[A-Za-z]{2,}', text) and (' ' in text.strip() or re.match(r'^[A-Z][a-z]+$', text)):
                        line = src.count('\n', 0, i) + 1
                        print('%s:%d: %s' % (os.path.relpath(f, ROOT), line, text[:110].replace('\n', '\\n')))
                    i = end; continue
                i += 1
    else:
        print(__doc__)


if __name__ == '__main__':
    main()
