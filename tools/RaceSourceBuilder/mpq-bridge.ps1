param(
    [Parameter(Mandatory=$true)][ValidateSet('extract-match','extract-prefix','extract-files','pack')][string]$Action,
    [string]$Mpq,
    [string]$Root,
    [string]$Match,
    [string]$NamesFile,
    [string]$Out
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mpqSource = Get-Content (Join-Path $repo 'src\Mpq.cs') -Raw
$bridge = @'
public static class L
{
    public static string F(string text, params object[] args) { return string.Format(text, args); }
}
public static class MpqBridge
{
    public static void ExtractMatching(string mpqPath, string root, string match)
    {
        Directory.CreateDirectory(root);
        using (var mpq = new MpqArchive(mpqPath))
        {
            int n = 0;
            foreach (var name in mpq.Names())
            {
                if (name.StartsWith("DBFilesClient", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var data = mpq.Read(name);
                if (data == null) continue;
                var to = Path.Combine(root, name.Replace('\\', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllBytes(to, data);
                n++;
            }
            Console.WriteLine("extracted " + n);
        }
    }
    public static void ExtractPrefix(string mpqPath, string root, string prefix)
    {
        Directory.CreateDirectory(root);
        using (var mpq = new MpqArchive(mpqPath))
        {
            int n = 0;
            foreach (var name in mpq.Names())
            {
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var data = mpq.Read(name);
                if (data == null) continue;
                var to = Path.Combine(root, name.Replace('\\', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllBytes(to, data);
                n++;
            }
            Console.WriteLine("extracted " + n);
        }
    }
    public static void ExtractFiles(string mpqPath, string root, string namesFile)
    {
        Directory.CreateDirectory(root);
        using (var mpq = new MpqArchive(mpqPath))
        {
            int n = 0;
            foreach (var name in File.ReadAllLines(namesFile))
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var data = mpq.Read(name);
                if (data == null) { Console.WriteLine("missing " + name); continue; }
                var to = Path.Combine(root, name.Replace('\\', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllBytes(to, data);
                n++;
            }
            Console.WriteLine("extracted " + n);
        }
    }
    public static void Pack(string root, string output)
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = file.Substring(root.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
            sources[rel.Replace('/', '\\')] = file;
        }
        MpqArchive.WriteFromFiles(output, sources, new Dictionary<string, byte[]>());
        Console.WriteLine("packed " + sources.Count + " files, " + new FileInfo(output).Length + " bytes");
    }
}
'@
$last = $mpqSource.LastIndexOf('}')
$combined = $mpqSource.Substring(0, $last) + "`n" + $bridge + "`n" + $mpqSource.Substring($last)
Add-Type -TypeDefinition $combined -Language CSharp

switch ($Action) {
    'extract-match'  { [CoAInstaller.MpqBridge]::ExtractMatching($Mpq, $Root, $Match) }
    'extract-prefix' { [CoAInstaller.MpqBridge]::ExtractPrefix($Mpq, $Root, $Match) }
    'extract-files'  { [CoAInstaller.MpqBridge]::ExtractFiles($Mpq, $Root, $NamesFile) }
    'pack'          { [CoAInstaller.MpqBridge]::Pack($Root, $Out) }
}
