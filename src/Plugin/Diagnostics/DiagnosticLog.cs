using System.Globalization;
using System.Text;

namespace MapDecals;

/// <summary>Bounded, single-line logging with rotation; no game/native dependencies.</summary>
internal sealed class DiagnosticLog(string path, Action<string> console, Action<string> warning,
    long maxBytes = 5 * 1024 * 1024)
{
    private readonly object _sync = new();
    private long _sequence;
    private bool _warned;
    internal string Path { get; } = path;

    internal static string Clean(string? text, int limit = 4096)
    {
        if (text == null) return "<null>";
        var output = new StringBuilder(Math.Min(text.Length, limit));
        foreach (char c in text)
        {
            var part = c switch { '\r' => "\\r", '\n' => "\\n", '\t' => "\\t", _ => char.IsControl(c) ? "?" : c.ToString() };
            if (output.Length + part.Length > limit) { output.Append("...[truncated]"); break; }
            output.Append(part);
        }
        return output.ToString();
    }

    internal void Write(string stage, string message, bool toFile)
    {
        lock (_sync)
        {
            string line = $"[MDDBG] {DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)} #{++_sequence} {Clean(stage, 48)} {Clean(message)}";
            try { console(line); } catch { /* Diagnostics must not break plugin behavior. */ }
            if (!toFile) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length >= maxBytes)
                {
                    if (File.Exists(Path + ".2")) File.Delete(Path + ".2");
                    if (File.Exists(Path + ".1")) File.Move(Path + ".1", Path + ".2");
                    File.Move(Path, Path + ".1");
                }
                File.AppendAllText(Path, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    try { warning($"MapDecals debug file unavailable ({ex.GetType().Name}); use server console logs."); } catch { }
                }
            }
        }
    }
}

internal static class DiagnosticPaths
{
    internal static string? CompiledMaterial(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string path = input.Trim().Replace('\\', '/');
        if (!path.StartsWith("materials/", StringComparison.OrdinalIgnoreCase)
            || path.Split('/').Any(x => x is ".." or ".") || path.Contains(':') || path.Any(char.IsControl)) return null;
        if (path.EndsWith(".vmat_c", StringComparison.OrdinalIgnoreCase)) return path;
        return path.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase) ? path + "_c" : null;
    }
}
