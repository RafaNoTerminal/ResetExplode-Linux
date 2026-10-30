using System.Diagnostics;
using System.Text;
namespace Diagnostico.Windows;

internal record MaintenanceExecution(int Code, string Output, string Error);
internal static class MaintenanceRunner
{
    internal static async Task<MaintenanceExecution> Execute(MaintenanceAction item, Action<string> progress, string? fixtureFile = null, string[]? fixtureArguments = null)
    {
        var start = new ProcessStartInfo(fixtureFile ?? Path.Combine(Environment.SystemDirectory, item.File)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        start.StandardOutputEncoding = item.UnicodeOutput ? Encoding.Unicode : Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        start.StandardErrorEncoding = start.StandardOutputEncoding;
        foreach (string arg in fixtureArguments ?? item.Arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Comando não iniciou.");
        async Task<string> Pump(StreamReader reader) {
            var full = new StringBuilder(); char[] buffer = new char[256]; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0) {
                string chunk = new(buffer, 0, count); full.Append(chunk);
                if (full.Length > 1048576) full.Remove(0, full.Length - 1048576);
                progress(chunk.Replace("\0", ""));
            }
            return full.ToString().Replace("\0", "");
        }
        var stdout = Pump(process.StandardOutput); var stderr = Pump(process.StandardError);
        // Servicing is not killed on cancellation: wait for Windows to finish safely.
        await process.WaitForExitAsync();
        return new(process.ExitCode, await stdout, await stderr);
    }
    internal static string CurrentCbs(DateTime since)
    {
        try {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS", "CBS.log");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long offset = Math.Max(0, file.Length - 1048576); file.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(file); if (offset > 0) reader.ReadLine();
            var rows = new List<string>(); string? line;
            while ((line = reader.ReadLine()) is not null) {
                if (line.Contains("[SR]") && line.Length >= 19 && DateTime.TryParseExact(line[..19], "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at) && at >= since.AddSeconds(-1)) rows.Add(line);
            }
            return rows.Count == 0 ? "Nenhuma linha [SR] desta execução disponível no trecho recente do CBS. Consulte o log completo." : string.Join(Environment.NewLine, rows.TakeLast(150));
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "Não foi possível ler CBS.log: " + e.Message; }
    }
}
