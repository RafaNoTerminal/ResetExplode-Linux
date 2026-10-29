using System.Diagnostics;
using System.Text;
namespace Diagnostico.Windows;

internal record TempCandidate(string Path, long Bytes, DateTime Written);
internal static class TempCleanup
{
    internal static string[] Roots => [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")];
    internal static List<TempCandidate> Preview(string[]? roots = null)
    {
        var rows = new List<TempCandidate>(); var cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (string root in roots ?? Roots) {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) continue;
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0 && rows.Count < 50000) {
                string directory = pending.Pop();
                try {
                    foreach (string p in Directory.EnumerateFileSystemEntries(directory)) {
                        try {
                            var attributes = File.GetAttributes(p); if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if ((attributes & FileAttributes.Directory) != 0) pending.Push(p);
                            else { var f = new FileInfo(p); if (f.LastWriteTimeUtc < cutoff) rows.Add(new(f.FullName, f.Length, f.LastWriteTimeUtc)); }
                        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    }
                } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return rows;
    }
    internal static TestResult Apply(TempCandidate[] candidates)
    {
        long bytes = 0; int removed = 0, skipped = 0;
        foreach (var c in candidates) {
            try {
                string path = Path.GetFullPath(c.Path); string? root = Roots.FirstOrDefault(r => path.StartsWith(Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                if (root is null) throw new IOException("Fora das pastas temporárias permitidas.");
                bool link = false; for (var d = new DirectoryInfo(Path.GetDirectoryName(path)!); d is not null && d.FullName.Length >= root.Length; d = d.Parent) if ((d.Attributes & FileAttributes.ReparsePoint) != 0) { link = true; break; }
                var f = new FileInfo(path); if (link || !f.Exists || (f.Attributes & FileAttributes.ReparsePoint) != 0 || f.LastWriteTimeUtc != c.Written || f.Length != c.Bytes || f.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7)) { skipped++; continue; }
                f.Delete(); bytes += c.Bytes; removed++;
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return TestResult.Done("Limpeza de temporários", ("Arquivos removidos", removed), ("Ignorados/em uso/alterados", skipped), ("MiB liberados", Math.Round(bytes / 1048576.0, 2)), ("Escopo", "Apenas arquivos da prévia com mais de7dias, nas pastas Temp permitidas. Não esvazia Lixeira, Downloads, documentos ou cache do Windows Update."));
    }
}

public sealed partial class MainForm
{
    string maintenanceLog = ""; List<TempCandidate> cleanupCandidates = [];
    void BuildMaintenance()
    {
        var log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Surface, ForeColor = Ink, Font = new Font("Consolas", 10), Text = maintenanceLog }; content.Controls.Add(log);
        var actions = Actions(); var choices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 }; choices.Items.AddRange(MaintenanceCatalog.All); choices.SelectedIndex = 0; actions.Controls.Add(choices);
        Button(actions, "Executar seleção", () => {
            if (task is not null || choices.SelectedItem is not MaintenanceAction item) return;
            if (MessageBox.Show(this, item.Title + "\n\n" + item.Effect + "\n\nExecutar no Windows atual?", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            _ = Run(item.Title, async ct => {
                maintenanceLog = ""; status.Text = item.Title + " · aguarde a conclusão…";
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, item.File)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                if (item.UnicodeOutput) start.StandardOutputEncoding = Encoding.Unicode;
                foreach (var argument in item.Arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Comando não iniciou.");
                async Task<string> Read(StreamReader reader) { var output = new StringBuilder(); while (await reader.ReadLineAsync() is { } line) { line = ActivationFlow.Clean(line); output.AppendLine(line); maintenanceLog += line + Environment.NewLine; if (maintenanceLog.Length > 1048576) maintenanceLog = maintenanceLog[^1048576..]; if (!log.IsDisposed) { log.Text = maintenanceLog; log.SelectionStart = log.TextLength; log.ScrollToCaret(); } } return output.ToString(); }
                var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError); await process.WaitForExitAsync();
                return new(item.Title, process.ExitCode is 0 or 3010 ? "Comando concluiu; conferir diagnóstico" : "Falha no comando", new() { ["Retorno"] = process.ExitCode, ["Saída"] = await stdout, ["Erros"] = await stderr, ["Observação"] = "Código de saída não é aprovação geral do PC. Interprete a saída e reteste; reinício somente manual." }, DateTimeOffset.Now);
            });
        }, true);
        var cleanup = Actions();
        Button(cleanup, "Prévia de temporários", () => _ = Run("Prévia de limpeza", async ct => { cleanupCandidates = await Task.Run(() => TempCleanup.Preview(), ct); maintenanceLog = $"{cleanupCandidates.Count} arquivos com mais de7dias · {cleanupCandidates.Sum(f => f.Bytes) / 1048576.0:N1} MiB\r\n" + string.Join("\r\n", cleanupCandidates.Take(500).Select(f => f.Path)); if (!log.IsDisposed) log.Text = maintenanceLog; return TestResult.Done("Prévia de temporários", ("Arquivos candidatos", cleanupCandidates.Count), ("MiB", cleanupCandidates.Sum(f => f.Bytes) / 1048576.0), ("Alterações", "Nenhuma")); }), true);
        Button(cleanup, "Limpar somente a prévia", () => {
            if (task is not null || cleanupCandidates.Count == 0) return;
            if (MessageBox.Show(this, "Excluir " + cleanupCandidates.Count + " arquivos temporários da prévia, com mais de7dias?\n\nArquivos em uso, recentes, links e itens alterados serão preservados. A exclusão não pode ser desfeita pelo app. Lixeira, Downloads, documentos e cache de atualizações ficam preservados.", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            var selected = cleanupCandidates.ToArray(); _ = Run("Limpeza de temporários", async ct => { var result = await Task.Run(() => TempCleanup.Apply(selected)); cleanupCandidates.Clear(); maintenanceLog = OperationResults.Summary(result); if (!log.IsDisposed) log.Text = maintenanceLog; return result; });
        }, true);
        var diagnostics = Actions();
        Button(diagnostics, "Segurança / serviços / inicialização", () => _ = Run("Diagnóstico do Windows", async ct => {
            var script = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$rows=@();try{$rows+= [pscustomobject]@{Grupo='Defender';Dados=(Get-MpComputerStatus|Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureLastUpdated)}}catch{$rows+=[pscustomobject]@{Grupo='Defender';Erro=$_.Exception.Message}};try{$rows+=[pscustomobject]@{Grupo='Firewall';Dados=@(Get-NetFirewallProfile|Select-Object Name,Enabled)}}catch{$rows+=[pscustomobject]@{Grupo='Firewall';Erro=$_.Exception.Message}};$rows+=[pscustomobject]@{Grupo='Serviços';Dados=@(Get-Service|Where-Object {$_.Name -in @('Spooler','wuauserv','bits','WinDefend','Dnscache','LanmanWorkstation','W32Time')}|Select-Object Name,Status,StartType)};try{$rows+=[pscustomobject]@{Grupo='Discos';Dados=@(Get-PhysicalDisk|Select-Object FriendlyName,MediaType,Size,HealthStatus,OperationalStatus)}}catch{$rows+=[pscustomobject]@{Grupo='Discos';Erro=$_.Exception.Message}};$rows+=[pscustomobject]@{Grupo='Inicialização';Dados=@(Get-CimInstance Win32_StartupCommand|Select-Object Name,Location)};ConvertTo-Json -InputObject $rows -Depth 7 -Compress";
            var outcome = await UpdateProcess(script, ct); if (outcome.ExitCode != 0) throw new IOException(outcome.Error);
            maintenanceLog = outcome.Output; if (!log.IsDisposed) log.Text = maintenanceLog;
            return TestResult.Done("Segurança / serviços / inicialização", ("Consulta", System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(outcome.Output)), ("Alterações", "Nenhuma"));
        }), true);
        Heading("Manutenção do Windows", "Baseada no script de limpeza/corretiva do Helpdesk, com prévia, escopo explícito e códigos reais. SFC/DISM podem demorar. O app aguarda o comando terminar e não reinicia o PC automaticamente. Diagnóstico de segurança é somente leitura.");
    }
}
