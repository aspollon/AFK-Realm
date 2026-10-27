using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The database editor: browse and edit tables, run any SQL, export, import, and undo the last change.
    /// The page keeps what is being edited; here every change is turned into statements and, before it is carried
    /// out, into the statements that take it back (the undo files in Backups/undo).
    /// </summary>
    static partial class Api
    {
        const int DbPageSize = 500, DbQueryRows = 5000;
        const string RowsMark = "undo (rows)", TablesMark = "undo (tables)";
        static readonly object dbLock = new object();

        class Col { public string Name, Type; public bool Nullable, Key, Binary, Auto; }

        static string Id(string name) { return "`" + name.Replace("`", "``") + "`"; }
        static DbLogin Login(Install inst) { return DbLogin.FromConfig(inst); }

        static Dictionary<string, List<string>> DbTables(Install inst)
        {
            var found = new Dictionary<string, List<string>>();
            var t = MySql.Tables(inst, Login(inst), "SELECT TABLE_SCHEMA, TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA NOT IN " +
                "('information_schema', 'performance_schema', 'mysql', 'sys') ORDER BY TABLE_SCHEMA, TABLE_NAME;", 100000);
            foreach (var row in t.Count > 0 ? t[0].Rows : new List<string[]>())
            {
                if (!found.ContainsKey(row[0])) found[row[0]] = new List<string>();
                found[row[0]].Add(row[1]);
            }
            return found;
        }

        static IEnumerable<string> DatabasesInOrder(Dictionary<string, List<string>> tablesOf)
        {
            string[] first = { "acore_world", "acore_characters", "acore_auth", "acore_playerbots" };
            return first.Where(tablesOf.ContainsKey).Concat(tablesOf.Keys.Where(k => !first.Contains(k)).OrderBy(k => k));
        }

        static string Friendly(string database)
        {
            switch (database)
            {
                case "acore_world": return L.T("World: creatures, items, quests, loot") + "  (acore_world)";
                case "acore_characters": return L.T("Characters and what they own") + "  (acore_characters)";
                case "acore_auth": return L.T("Accounts and realm") + "  (acore_auth)";
                case "acore_playerbots": return "Playerbots  (acore_playerbots)";
                case "afk_modules": return L.T("AFK Realm's record of module changes") + "  (afk_modules)";
                default: return database;
            }
        }

        static List<Col> Columns(Install inst, string database, string name)
        {
            var meta = MySql.Tables(inst, Login(inst), "SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_KEY, EXTRA FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = " +
                MySql.Quote(database) + " AND TABLE_NAME = " + MySql.Quote(name) + " ORDER BY ORDINAL_POSITION;", 5000);
            var cols = (meta.Count > 0 ? meta[0].Rows : new List<string[]>()).Select(r => new Col
            {
                Name = r[0], Type = r[1] ?? "", Nullable = r[2] == "YES", Key = r[3] == "PRI", Auto = (r[4] ?? "").IndexOf("auto_increment", StringComparison.OrdinalIgnoreCase) >= 0,
                Binary = Regex.IsMatch(r[1] ?? "", @"^(tiny|medium|long)?blob|^(var)?binary|^bit", RegexOptions.IgnoreCase)
            }).ToList();
            if (cols.Count == 0) throw new UserError(L.F("The table {0} was not found.", name));
            return cols;
        }

        static string Literal(string value, Col column)
        {
            if (value == null) return "NULL";
            if (column.Binary && Regex.IsMatch(value, "^0x([0-9A-Fa-f]{2})*$")) return value.Length == 2 ? "''" : value;
            return MySql.Quote(value);
        }

        static string KeyClause(List<Col> columns, string[] original)
        {
            var parts = new List<string>();
            for (int i = 0; i < columns.Count; i++)
                if (columns[i].Key) parts.Add(Id(columns[i].Name) + " <=> " + Literal(original[i], columns[i]));
            return string.Join(" AND ", parts);
        }

        /// <summary>A row as the page sends it: an array of strings, null for NULL.</summary>
        static string[] RowOf(System.Text.Json.JsonElement e, int count)
        {
            var r = new string[count];
            if (e.ValueKind != System.Text.Json.JsonValueKind.Array) throw new UserError("Bad row.");
            int i = 0;
            foreach (var v in e.EnumerateArray())
            {
                if (i >= count) break;
                r[i++] = v.ValueKind == System.Text.Json.JsonValueKind.Null ? null : v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : v.ToString();
            }
            return r;
        }

        static IEnumerable<System.Text.Json.JsonElement> Items(System.Text.Json.JsonElement e)
        {
            return e.ValueKind == System.Text.Json.JsonValueKind.Array ? e.EnumerateArray() : Enumerable.Empty<System.Text.Json.JsonElement>();
        }

        /// <summary>Stops world- and authserver before a change when the page asked for it.</summary>
        static void StopFirst(Args a)
        {
            if (!a.Bool("stop") || Ctl.World == null) return;
            try { Ctl.StopAll(text => { }, false); }
            catch (Exception ex) { throw new UserError(L.T("The server could not be stopped, so nothing was changed:") + "\n\n" + ex.Message); }
        }

        // ---------------------------------------------------------------- SQL files: what they do

        internal class FilePlan
        {
            public readonly SortedSet<string> Rebuilt = new SortedSet<string>(), Created = new SortedSet<string>(), Written = new SortedSet<string>(),
                Emptied = new SortedSet<string>(), Altered = new SortedSet<string>(), Databases = new SortedSet<string>();
            public string Named;
            public bool Partial, Other, Loose;
        }

        static FilePlan Scan(TextReader r)
        {
            const long limit = 96L * 1024 * 1024;
            var plan = new FilePlan();
            var verb = new Regex(@"(?:^|;)\s*(DROP\s+TABLE(?:\s+IF\s+EXISTS)?|CREATE\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?|INSERT(?:\s+IGNORE)?\s+INTO|REPLACE\s+INTO|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?|ALTER\s+TABLE|UPDATE|USE|DROP\s+DATABASE|CREATE\s+DATABASE(?:\s+IF\s+NOT\s+EXISTS)?)\s+((?:`[^`]+`|[A-Za-z0-9_$]+)(?:\.(?:`[^`]+`|[A-Za-z0-9_$]+))?)", RegexOptions.IgnoreCase);
            long read = 0; string line, use = null;
            while ((line = r.ReadLine()) != null)
            {
                read += line.Length + 1;
                if (read > limit) { plan.Partial = true; break; }
                var named = Regex.Match(line.Length > 200 ? line.Substring(0, 200) : line, @"^--\s*(?:Host:.*)?Database:\s*`?([A-Za-z0-9_$]+)");
                if (named.Success && plan.Named == null) plan.Named = named.Groups[1].Value;
                foreach (Match m in verb.Matches(line))
                {
                    string what = Regex.Replace(m.Groups[1].Value.ToUpperInvariant(), @"\s+", " "), target = m.Groups[2].Value.Replace("`", "");
                    if (what == "USE") { use = target; plan.Databases.Add(target); continue; }
                    if (what.Contains("DATABASE")) { plan.Databases.Add(target); plan.Other = true; continue; }
                    if (target.Contains(".")) plan.Databases.Add(target.Substring(0, target.IndexOf('.')));
                    else if (use != null) target = use + "." + target;
                    else plan.Loose = true;
                    if (what.StartsWith("DROP TABLE")) plan.Rebuilt.Add(target);
                    else if (what.StartsWith("CREATE TABLE")) plan.Created.Add(target);
                    else if (what.StartsWith("DELETE") || what.StartsWith("TRUNCATE")) plan.Emptied.Add(target);
                    else if (what.StartsWith("ALTER")) plan.Altered.Add(target);
                    else plan.Written.Add(target);
                }
            }
            plan.Created.ExceptWith(plan.Rebuilt);
            return plan;
        }

        static bool ReadsOnly(string text)
        {
            string bare = Regex.Replace(text, @"'(?:[^'\\]|\\.|'')*'|""(?:[^""\\]|\\.|"""")*""|`[^`]*`|/\*.*?\*/|--[^\r\n]*|#[^\r\n]*", " ", RegexOptions.Singleline);
            foreach (var statement in bare.Split(';'))
            {
                string s = statement.Trim();
                if (s.Length == 0) continue;
                if (!Regex.IsMatch(s, @"^\(*\s*(select|show|describe|desc|explain|use|with|table|values)\b", RegexOptions.IgnoreCase)) return false;
                if (Regex.IsMatch(s, @"\binto\s+(outfile|dumpfile)\b|\bfor\s+update\b", RegexOptions.IgnoreCase)) return false;
            }
            return true;
        }

        static Dictionary<string, SortedSet<string>> Touched(FilePlan plan, string fallback)
        {
            var touched = new Dictionary<string, SortedSet<string>>();
            foreach (var name in plan.Rebuilt.Concat(plan.Created).Concat(plan.Altered).Concat(plan.Emptied).Concat(plan.Written))
            {
                int dot = name.IndexOf('.');
                string d = dot < 0 ? fallback : name.Substring(0, dot), t = name.Substring(dot + 1);
                if (d == null) continue;
                if (!touched.ContainsKey(d)) touched[d] = new SortedSet<string>();
                touched[d].Add(t);
            }
            return touched;
        }

        static string Names(IEnumerable<string> names)
        {
            var list = names.ToList();
            return string.Join(", ", list.Take(12)) + (list.Count > 12 ? " and " + (list.Count - 12) + " more" : "");
        }

        static string SizeText(long bytes) { return bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : Math.Max(1, bytes / 1024) + " KB"; }

        // ---------------------------------------------------------------- undo files

        static string UndoFolder(Install inst) { return Path.Combine(inst.Root, "Backups", "undo"); }

        static List<string> UndoFiles(Install inst)
        {
            string dir = UndoFolder(inst);
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.sql").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        static string NewUndoFile(Install inst, string label)
        {
            foreach (char c in Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })) label = label.Replace(c, '_');
            if (label.Length > 90) label = label.Substring(0, 90);
            Directory.CreateDirectory(UndoFolder(inst));
            return Path.Combine(UndoFolder(inst), DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + " " + label.Trim() + ".sql");
        }

        static void PruneUndo(Install inst)
        {
            long total = 0; int n = 0;
            foreach (var f in UndoFiles(inst))
            {
                try { total += new FileInfo(f).Length; if (++n > 10 || (n > 1 && total > 2L * 1024 * 1024 * 1024)) File.Delete(f); } catch { }
            }
        }

        static void WriteCopies(Install inst, string undoFile, Dictionary<string, SortedSet<string>> touched, Dictionary<string, List<string>> known)
        {
            try
            {
                using (var dst = File.Create(undoFile))
                {
                    var enc = new UTF8Encoding(false);
                    Action<string> write = line => { var b = enc.GetBytes(line + "\n"); dst.Write(b, 0, b.Length); };
                    write("-- " + Product.Name + " " + TablesMark + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ", databases: " + string.Join(" ", touched.Keys));
                    foreach (var d in touched.Keys)
                    {
                        if (!known.ContainsKey(d)) continue;
                        if (touched[d] == null) { MySql.DumpTables(inst, Login(inst), d, null, dst); continue; }
                        var there = touched[d].Where(known[d].Contains).ToList();
                        write("USE " + Id(d) + ";");
                        foreach (var t in touched[d].Where(t => !known[d].Contains(t))) write("DROP TABLE IF EXISTS " + Id(t) + ";");
                        if (there.Count > 0) MySql.DumpTables(inst, Login(inst), d, there, dst);
                    }
                }
            }
            catch { try { File.Delete(undoFile); } catch { } throw; }
            PruneUndo(inst);
        }

        static object UndoInfo(Install inst)
        {
            var files = UndoFiles(inst);
            if (files.Count == 0) return new { count = 0 };
            string file = files[0], name = Path.GetFileNameWithoutExtension(file), what = name.Length > 16 ? name.Substring(16) : name; DateTime when;
            string at = DateTime.TryParseExact(name.Length >= 15 ? name.Substring(0, 15) : "", "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when) ? when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";
            string head = "";
            try { using (var r = new StreamReader(file)) head = r.ReadLine() ?? ""; } catch { }
            int mark = head.IndexOf("databases: ", StringComparison.Ordinal);
            return new { count = files.Count, what, at, rows = head.Contains(RowsMark), databases = mark < 0 ? new string[0] : head.Substring(mark + 11).Split(' ') };
        }

        // ---------------------------------------------------------------- the routes

        static void MapDatabase(WebApplication app)
        {
            Get(app, "/api/db/tables", c =>
            {
                var inst = Inst;
                if (Ctl.Db == null) throw new UserError(L.T("The database is not running. Start the server first."));
                Dictionary<string, List<string>> found;
                try { found = DbTables(inst); }
                catch (Exception ex) { throw new UserError(L.T("The databases could not be read:") + " " + ex.Message); }
                return new
                {
                    databases = DatabasesInOrder(found).Select(d => new { name = d, friendly = Friendly(d), tables = found[d] }).ToArray(),
                    undo = UndoInfo(inst), worldRunning = Ctl.World != null
                };
            });

            Post(app, "/api/db/page", a =>
            {
                var inst = Inst;
                string database = a.Str("db") ?? "", name = a.Str("table") ?? "", filter = (a.Str("where") ?? "").Trim();
                int page = Math.Max(0, a.Int("page"));
                var cols = Columns(inst, database, name);
                string order = cols.Any(x => x.Key) ? " ORDER BY " + string.Join(", ", cols.Where(x => x.Key).Select(x => Id(x.Name))) : "";
                List<SqlTable> data;
                try
                {
                    // Read-only session: the WHERE text is the user's.
                    data = MySql.Tables(inst, Login(inst), "SET SESSION TRANSACTION READ ONLY;\nSELECT * FROM " + Id(database) + "." + Id(name) + (filter.Length > 0 ? " WHERE " + filter : "") + order +
                        " LIMIT " + (DbPageSize + 1) + " OFFSET " + ((long)page * DbPageSize) + ";", DbPageSize + 1);
                }
                catch (Exception ex) { throw new UserError(L.T("Not read:") + " " + ex.Message); }
                var rows = data.Count > 0 ? data[data.Count - 1].Rows : new List<string[]>();
                return new
                {
                    columns = cols.Select(x => new { name = x.Name, type = x.Type, nullable = x.Nullable, key = x.Key, binary = x.Binary, auto = x.Auto }).ToArray(),
                    rows = rows.Take(DbPageSize).Select(r => r.Take(cols.Count).ToArray()).ToArray(),
                    more = rows.Count > DbPageSize, pageSize = DbPageSize
                };
            });

            // The page sends what changed: deleted rows (as read), changed rows (as read, now, which columns), added rows.
            Post(app, "/api/db/save", a =>
            {
                var inst = Inst;
                string db = a.Str("db") ?? "", table = a.Str("table") ?? "";
                var columns = Columns(inst, db, table);
                if (!columns.Any(x => x.Key)) throw new UserError(L.T("This table has no primary key, so rows cannot be told apart: it can be read here and changed in the SQL tab."));
                string target = Id(db) + "." + Id(table);
                var statements = new List<string>(); var back = new List<string>(); bool wholeTable = false;
                int updates = 0, inserts = 0, deletes = 0;
                string allColumns = string.Join(", ", columns.Select(x => Id(x.Name)));
                foreach (var e in Items(a.Raw("deleted")))
                {
                    var original = RowOf(e, columns.Count);
                    statements.Add("DELETE FROM " + target + " WHERE " + KeyClause(columns, original) + " LIMIT 1;");
                    back.Add("INSERT INTO " + target + " (" + allColumns + ") VALUES (" + string.Join(", ", columns.Select((x, i) => Literal(original[i], x))) + ");");
                    ++deletes;
                }
                foreach (var e in Items(a.Raw("changed")))
                {
                    var c = new Args(e);
                    var original = RowOf(c.Raw("original"), columns.Count);
                    var now = RowOf(c.Raw("values"), columns.Count);
                    var which = c.List("columns").Select(s => int.Parse(s, CultureInfo.InvariantCulture)).Where(i => i >= 0 && i < columns.Count && !columns[i].Binary).Distinct().ToList();
                    if (which.Count == 0) continue;
                    statements.Add("UPDATE " + target + " SET " + string.Join(", ", which.Select(i => Id(columns[i].Name) + " = " + Literal(now[i], columns[i]))) + " WHERE " + KeyClause(columns, original) + " LIMIT 1;");
                    ++updates;
                    var after = (string[])original.Clone();
                    foreach (var i in which) after[i] = now[i];
                    back.Add("UPDATE " + target + " SET " + string.Join(", ", which.Select(i => Id(columns[i].Name) + " = " + Literal(original[i], columns[i]))) + " WHERE " + KeyClause(columns, after) + " LIMIT 1;");
                }
                foreach (var e in Items(a.Raw("added")))
                {
                    var now = RowOf(e, columns.Count);
                    var names = new List<string>(); var values = new List<string>();
                    for (int i = 0; i < columns.Count; i++)
                    {
                        if (now[i] == "") continue;                   // left empty: the column's default
                        names.Add(Id(columns[i].Name)); values.Add(Literal(now[i], columns[i]));
                    }
                    if (names.Count == 0) continue;
                    statements.Add("INSERT INTO " + target + " (" + string.Join(", ", names) + ") VALUES (" + string.Join(", ", values) + ");");
                    ++inserts;
                    if (columns.Where((x, i) => x.Key && string.IsNullOrEmpty(now[i])).Any()) wholeTable = true;
                    else back.Add("DELETE FROM " + target + " WHERE " + KeyClause(columns, now) + " LIMIT 1;");
                }
                if (statements.Count == 0) return new { saved = 0 };
                lock (dbLock)
                {
                    StopFirst(a);
                    string script = "START TRANSACTION;\n" + string.Join("\n", statements) + "\nCOMMIT;\n";
                    string undoFile = NewUndoFile(inst, L.F("edit of {0}", db + "." + table));
                    back.Reverse();
                    if (wholeTable) WriteCopies(inst, undoFile, new Dictionary<string, SortedSet<string>> { { db, new SortedSet<string> { table } } }, DbTables(inst));
                    else
                    {
                        File.WriteAllText(undoFile, "-- " + Product.Name + " " + RowsMark + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ", databases: " + db +
                            "\nSTART TRANSACTION;\n" + string.Join("\n", back) + "\nCOMMIT;\n", new UTF8Encoding(false));
                        PruneUndo(inst);
                    }
                    try { MySql.Tables(inst, Login(inst), script, 1); }
                    catch (Exception ex)
                    {
                        try { File.Delete(undoFile); } catch { }
                        throw new UserError(L.T("The database refused the changes; nothing was written (tables of the MyISAM kind keep what came before the error):") + "\n\n" + ex.Message);
                    }
                }
                return new { saved = statements.Count, updates, inserts, deletes, undo = UndoInfo(inst) };
            });

            Post(app, "/api/db/sql", a =>
            {
                var inst = Inst;
                string text = a.Str("text") ?? "";
                bool allow = a.Bool("allow"), force = a.Bool("force");
                if (text.Trim().Length == 0) return new { results = new object[0] };
                bool reads = ReadsOnly(text);
                if (!allow && !reads) throw new UserError(L.T("This would change something. Tick \"Allow changes\" at the top first."));
                Dictionary<string, SortedSet<string>> touched = null; string undoFile = null;
                var known = DbTables(inst);
                if (allow && !reads)
                {
                    var plan = Scan(new StringReader(text));
                    touched = Touched(plan, null);
                    if (plan.Other || plan.Loose || touched.Count == 0 || touched.Keys.Any(d => !known.ContainsKey(d)))
                    {
                        if (!force) throw new NeedsConfirmation((plan.Loose
                            ? L.F("{0} cannot tell for sure which tables this changes (name the database with each table: acore_world.creature_template), so \"Undo last change\" will not be able to take it back.", Product.Name)
                            : L.F("{0} cannot tell for sure which tables this changes, so \"Undo last change\" will not be able to take it back.", Product.Name)) +
                            "\n\n" + L.T("Run it anyway?"));
                        touched = null;
                    }
                    else undoFile = NewUndoFile(inst, L.F("SQL on {0}", string.Join(", ", touched.SelectMany(d => d.Value.Select(t => d.Key + "." + t)).Take(3))) + (touched.Sum(d => d.Value.Count) > 3 ? " " + L.T("and more") : ""));
                }
                DateTime start = DateTime.Now;
                string script = (allow ? "" : "SET SESSION TRANSACTION READ ONLY;\n") + text.TrimEnd() + "\n;SELECT ROW_COUNT() AS afk_rows_changed;\n";
                List<SqlTable> list;
                lock (dbLock)
                {
                    if (allow && !reads) StopFirst(a);
                    if (undoFile != null)
                    {
                        try { WriteCopies(inst, undoFile, touched, known); }
                        catch (Exception ex) { throw new UserError(L.T("The copy for \"Undo last change\" could not be made, so nothing was run.") + "\n\n" + ex.Message); }
                    }
                    try { list = MySql.Tables(inst, Login(inst), script, DbQueryRows); }
                    catch (Exception ex)
                    {
                        throw new UserError((undoFile != null && File.Exists(undoFile) ? L.T("Stopped with an error. Statements before it were carried out; \"Undo last change\" takes them back.") : L.T("Stopped with an error. Statements before it were carried out.")) + "\n\n" + ex.Message);
                    }
                }
                string changed = null;
                if (list.Count > 0 && list[list.Count - 1].Columns.Count == 1 && list[list.Count - 1].Columns[0] == "afk_rows_changed")
                {
                    string v = list[list.Count - 1].Rows.Count > 0 ? list[list.Count - 1].Rows[0][0] : null; int n;
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0) changed = L.F("{0} row(s) changed by the last statement", n);
                    list.RemoveAt(list.Count - 1);
                }
                string took = (DateTime.Now - start).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                string hint = L.F("Done in {0}", took) + (changed != null ? ": " + changed : "") + (list.Count == 0 && changed == null ? ". " + L.T("No rows came back.") : "") +
                    (list.Count > 0 ? ". " + (list[list.Count - 1].Total > DbQueryRows ? L.F("{0} row(s), the first {1} are shown.", list[list.Count - 1].Total, DbQueryRows) : L.F("{0} row(s).", list[list.Count - 1].Total)) : (list.Count == 0 && changed == null ? "" : "."));
                return new
                {
                    hint, changedSomething = allow && !reads, undo = UndoInfo(inst),
                    results = list.Select((t, i) => new
                    {
                        label = L.F("Result {0} of {1}, {2} row(s):", i + 1, list.Count, t.Total) + " " + (t.Statement.Length > 60 ? t.Statement.Substring(0, 60) + " …" : t.Statement),
                        columns = t.Columns, rows = t.Rows.Select(r => r.Take(t.Columns.Count).ToArray()).ToArray(), total = t.Total
                    }).ToArray()
                };
            });

            // chosen: [{db, tables: [..] or null for the whole database}]
            Post(app, "/api/db/export", a =>
            {
                var inst = Inst;
                var chosen = new Dictionary<string, List<string>>();
                foreach (var e in Items(a.Raw("chosen")))
                {
                    var c = new Args(e);
                    string d = c.Str("db");
                    if (string.IsNullOrEmpty(d)) continue;
                    chosen[d] = c.Has("tables") ? c.List("tables") : null;
                }
                if (chosen.Count == 0) throw new UserError(L.T("Nothing ticked yet."));
                string suggestion = chosen.Count == 1 ? chosen.Keys.First() + (chosen.Values.First() != null && chosen.Values.First().Count == 1 ? "." + chosen.Values.First()[0] : "") : "server-export";
                string file = Path.Combine(Files.Temp, Guid.NewGuid().ToString("N") + ".sql");
                try
                {
                    using (var dst = File.Create(file))
                    {
                        var head = new UTF8Encoding(false).GetBytes("-- " + Product.Name + " export\n");
                        dst.Write(head, 0, head.Length);
                        foreach (var name in chosen.Keys) MySql.DumpTables(inst, Login(inst), name, chosen[name], dst);
                    }
                }
                catch (Exception ex) { try { File.Delete(file); } catch { } throw new UserError(L.T("The export failed:") + "\n\n" + ex.Message); }
                long size = new FileInfo(file).Length;
                return new { url = Files.Offer(file, suggestion + ".sql", true), name = suggestion + ".sql", size = SizeText(size) };
            });

            // What a file (uploaded or on this computer) is going to do, and where it would go.
            Post(app, "/api/db/import/scan", a =>
            {
                var inst = Inst;
                string file = a.Str("file") ?? "";
                if (!File.Exists(file)) throw new UserError(L.T("The file could not be read:") + " " + file);
                FilePlan plan;
                try { using (var r = new StreamReader(file, new UTF8Encoding(false))) plan = Scan(r); }
                catch (Exception ex) { throw new UserError(L.T("The file could not be read:") + "\n\n" + ex.Message); }
                var tablesOf = DbTables(inst);
                var parts = new List<string>();
                if (plan.Rebuilt.Count > 0) parts.Add(L.F("replaces {0} table(s) completely", plan.Rebuilt.Count));
                if (plan.Created.Count > 0) parts.Add(L.F("creates {0} new table(s)", plan.Created.Count));
                if (plan.Altered.Count > 0) parts.Add(L.F("changes the structure of {0} table(s)", plan.Altered.Count));
                if (plan.Emptied.Count > 0) parts.Add(L.F("deletes rows from {0} table(s)", plan.Emptied.Count));
                int writes = plan.Written.Count(t => !plan.Rebuilt.Contains(t) && !plan.Created.Contains(t));
                if (writes > 0) parts.Add(L.F("writes rows into {0} table(s)", writes));
                if (plan.Other) parts.Add(L.T("creates or deletes whole databases"));
                string summary = parts.Count == 0 ? L.T("No changes to tables were recognised in this file; it may still do other things.")
                    : L.F("This file: {0}.", string.Join("; ", parts)) + (plan.Partial ? " " + L.T("(It is large; only its beginning was looked at.)") : "");
                var text = new StringBuilder();
                Action<string, ICollection<string>> say = (title, names) => { if (names.Count > 0) text.AppendLine(title + ": " + Names(names)); };
                say(L.T("Replaced completely"), plan.Rebuilt);
                say(L.T("Created"), plan.Created);
                say(L.T("Structure changed"), plan.Altered);
                say(L.T("Rows deleted from"), plan.Emptied);
                say(L.T("Rows written into"), plan.Written.Where(t => !plan.Rebuilt.Contains(t) && !plan.Created.Contains(t)).ToList());
                var named = plan.Databases.Where(tablesOf.ContainsKey).ToList();
                bool choose = plan.Loose || named.Count == 0;
                var order = DatabasesInOrder(tablesOf).ToList();
                var loose = plan.Rebuilt.Concat(plan.Created).Concat(plan.Altered).Concat(plan.Emptied).Concat(plan.Written).Where(t => !t.Contains(".")).Distinct().ToList();
                string home = order.Select(d => new { d, hits = loose.Count(t => tablesOf[d].Contains(t)) }).Where(x => x.hits > 0).OrderByDescending(x => x.hits).Select(x => x.d).FirstOrDefault();
                string guess = plan.Named != null && tablesOf.ContainsKey(plan.Named) ? plan.Named : home ?? (named.Count > 0 ? named[0] : tablesOf.ContainsKey("acore_world") ? "acore_world" : order.FirstOrDefault());
                return new
                {
                    name = a.Str("name") ?? Path.GetFileName(file), size = SizeText(new FileInfo(file).Length), summary, details = text.ToString().TrimEnd(),
                    choose, named, guess, fits = home != null ? L.F("Its tables are found in {0}.", home) : null,
                    databases = order.Select(d => new { name = d, friendly = Friendly(d) }).ToArray()
                };
            });

            Post(app, "/api/db/import", a =>
            {
                var inst = Inst;
                string file = a.Str("file") ?? "", shown = a.Str("name") ?? Path.GetFileName(file);
                if (!File.Exists(file)) throw new UserError(L.T("The file could not be read:") + " " + file);
                FilePlan plan;
                using (var r = new StreamReader(file, new UTF8Encoding(false))) plan = Scan(r);
                var known = DbTables(inst);
                var named = plan.Databases.Where(known.ContainsKey).ToList();
                bool choose = plan.Loose || named.Count == 0;
                string target = choose ? a.Str("target") : named[0];
                if (string.IsNullOrEmpty(target) || !known.ContainsKey(target)) throw new UserError(L.T("Which database is this file for?"));
                string where_ = choose ? target : string.Join(", ", named);
                var touched = Touched(plan, target);
                if (plan.Partial || plan.Other || touched.Count == 0) { touched.Clear(); foreach (var d in named.Concat(choose ? new[] { target } : new string[0]).Distinct()) touched[d] = null; }
                lock (dbLock)
                {
                    StopFirst(a);
                    string undoFile = NewUndoFile(inst, L.F("import of {0}", shown));
                    try { WriteCopies(inst, undoFile, touched, known); }
                    catch (Exception ex) { throw new UserError(L.T("The copy for \"Undo last change\" could not be made, so nothing was imported.") + "\n\n" + ex.Message); }
                    try { MySql.RunFile(inst, Login(inst), target, file); }
                    catch (Exception ex)
                    {
                        throw new UserError(L.T("The import stopped with an error. Statements before it were carried out, so the database may hold only a part of the file. \"Undo last change\" puts everything back as it was.") + "\n\n" + ex.Message);
                    }
                }
                if (file.StartsWith(Files.Temp, StringComparison.Ordinal)) { try { File.Delete(file); } catch { } }
                return new
                {
                    message = L.F("{0} was imported into {1}.", shown, where_) + "\n\n" + L.T("The worldserver reads most world tables only when it starts: restart the server for the change to show in the game.") + "\n\n" +
                        L.T("If it does not work out, \"Undo last change\" puts everything back as it was."),
                    hint = L.F("Imported {0} into {1}.", shown, where_), undo = UndoInfo(inst)
                };
            });

            Post(app, "/api/db/undo", a =>
            {
                var inst = Inst;
                var files = UndoFiles(inst);
                if (files.Count == 0) throw new UserError(L.T("There is no change to undo."));
                string file = files[0], name = Path.GetFileNameWithoutExtension(file), what = name.Length > 16 ? name.Substring(16) : name;
                var known = DbTables(inst);
                string any = known.ContainsKey("acore_world") ? "acore_world" : known.Keys.First();
                lock (dbLock)
                {
                    StopFirst(a);
                    try { MySql.RunFile(inst, Login(inst), any, file); }
                    catch (Exception ex) { throw new UserError(L.T("Undoing stopped with an error. The file is kept in") + "\n" + file + "\n\n" + ex.Message); }
                    try { File.Delete(file); } catch { }
                }
                return new { hint = L.F("Undone: {0}.", what), undo = UndoInfo(inst) };
            });
        }
    }
}
