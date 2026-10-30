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
    string maintenanceLog = "Pronto. Escolha uma verificação ou consulte segurança/serviços. Nenhum reparo iniciado.";
    List<TempCandidate> cleanupCandidates = [];
    TextBox? maintenanceView; Label? maintenanceStatus; MaintenanceAssessment? maintenanceAssessment;
    void AppendMaintenance(string text) {
        maintenanceLog += text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        if (maintenanceLog.Length > 1048576) maintenanceLog = maintenanceLog[^1048576..];
        if (maintenanceView is { IsDisposed: false } view) {
            bool follow = view.SelectionStart >= view.TextLength - 2;
            int caret = view.SelectionStart; view.Text = maintenanceLog;
            view.SelectionStart = follow ? view.TextLength : Math.Min(caret, view.TextLength);
            if (follow) view.ScrollToCaret();
        }
    }
    async Task<TestResult> ExecuteMaintenance(MaintenanceAction item) {
        DateTime began = DateTime.Now; var watch = Stopwatch.StartNew(); var pending = new StringBuilder();
        maintenanceLog = ""; AppendMaintenance($"[{began:T}] Iniciando: {item.Title}\r\n{item.File} {string.Join(" ", item.Arguments)}\r\nAguardando saída do Windows…\r\n");
        using var pulse = new System.Windows.Forms.Timer { Interval = 250 };
        void Flush() {
            if (pending.Length > 0) { string text = pending.ToString(); pending.Clear(); AppendMaintenance(text); }
            string state = item.Title + " · em execução há " + watch.Elapsed.ToString(@"mm\:ss") + " · aguardando o Windows";
            status.Text = state; if (maintenanceStatus is { IsDisposed: false }) maintenanceStatus.Text = state;
        }
        pulse.Tick += (_, _) => Flush(); pulse.Start();
        MaintenanceExecution output;
        try { output = await MaintenanceRunner.Execute(item, text => pending.Append(text)); Flush(); }
        catch (Exception e) {
            maintenanceAssessment = new("Execução não concluída", e.Message, []);
            AppendMaintenance("\r\nErro: " + e.Message + "\r\n");
            if (maintenanceStatus is { IsDisposed: false }) maintenanceStatus.Text = "Execução não concluída: " + e.Message;
            throw;
        }
        finally { pulse.Stop(); }
        maintenanceAssessment = MaintenanceAssessment.Read(item.Id, output.Code, output.Output + "\n" + output.Error);
        string cbs = item.Id.StartsWith("sfc") ? await Task.Run(() => MaintenanceRunner.CurrentCbs(began)) : @"Consulte C:\Windows\Logs\DISM\dism.log para os detalhes do DISM.";
        string summary = maintenanceAssessment.State + " — " + maintenanceAssessment.Detail;
        AppendMaintenance("\r\n\r\n" + summary + $"\r\nRetorno: {output.Code} (0x{unchecked((uint)output.Code):X8})\r\n" + cbs + "\r\n");
        if (maintenanceStatus is { IsDisposed: false }) maintenanceStatus.Text = summary;
        return new(item.Title, maintenanceAssessment.State, new() { ["Retorno"] = output.Code, ["Saída"] = output.Output, ["Erros"] = output.Error, ["Diagnóstico"] = maintenanceAssessment.Detail, ["Detalhes do log"] = cbs, ["Correções sugeridas"] = maintenanceAssessment.Suggested, ["Reinício"] = "Somente manual", ["Limite"] = "Windows repara conjuntos de arquivos; não permite escolher arquivos individuais neste fluxo." }, DateTimeOffset.Now);
    }
    void ReviewMaintenanceRepairs() {
        if (task is not null) return;
        using var dialog = new Form { Text = "Revisar e escolher correções", Size = new Size(830, 560), StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = Ink };
        var info = new TextBox { Dock = DockStyle.Top, Height = 165, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = (maintenanceAssessment?.State + "\r\n" + maintenanceAssessment?.Detail).Trim() + "\r\n\r\nEscolha as etapas desejadas. DISM repara a imagem (pode usar Windows Update/WSUS); SFC repara arquivos protegidos em conjunto. Os arquivos individuais ficam no CBS e não podem ser escolhidos pelo SFC. Depois será feita nova verificação SFC. O computador não reinicia automaticamente." };
        var choices = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Surface, ForeColor = Ink };
        foreach (string id in new[] { "dism-repair", "sfc-repair" }) choices.Items.Add(MaintenanceCatalog.Get(id), maintenanceAssessment?.Suggested.Contains(id) == true);
        var apply = new Button { Text = "Executar somente correções marcadas", Dock = DockStyle.Bottom, Height = 46, DialogResult = DialogResult.OK };
        dialog.Controls.Add(choices); dialog.Controls.Add(info); dialog.Controls.Add(apply);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var selected = choices.CheckedItems.Cast<MaintenanceAction>().ToArray(); if (selected.Length == 0) return;
        _ = Run("Correções selecionadas", async ct => {
            var steps = new List<TestResult>();
            foreach (var item in selected) {
                var result = await ExecuteMaintenance(item); steps.Add(result); results.Add(result);
                if (Convert.ToInt32(result.Dados["Retorno"]) != 0) break; // Includes restart pending: do not continue servicing.
            }
            if (steps.Count == selected.Length && steps.All(r => Convert.ToInt32(r.Dados["Retorno"]) == 0)) {
                var verify = await ExecuteMaintenance(MaintenanceCatalog.Get("sfc-verify")); steps.Add(verify); results.Add(verify);
            }
            return new TestResult("Correções selecionadas", maintenanceAssessment?.State ?? "Concluído; conferir etapas", new() { ["Resultados"] = steps.ToArray(), ["Alterações"] = "Apenas as etapas marcadas; verificação final somente leitura." }, DateTimeOffset.Now);
        });
    }
    void BuildMaintenance()
    {
        var log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Surface, ForeColor = Ink, Font = new Font("Consolas", 10), Text = maintenanceLog }; content.Controls.Add(log); maintenanceView = log;
        maintenanceStatus = new Label { Dock = DockStyle.Top, Height = 65, ForeColor = Accent, Text = maintenanceAssessment is null ? "Pronto para verificar. O andamento aparece desde o início da execução." : maintenanceAssessment.State + " — " + maintenanceAssessment.Detail }; content.Controls.Add(maintenanceStatus);
        var actions = Actions(); var choices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 370 }; choices.Items.AddRange(MaintenanceCatalog.All); choices.SelectedIndex = 0; actions.Controls.Add(choices);
        Button(actions, "Executar seleção", async () => {
            if (task is not null || choices.SelectedItem is not MaintenanceAction item) return;
            if (MessageBox.Show(this, item.Title + "\n\n" + item.Effect + "\n\nExecutar no Windows atual?", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            await Run(item.Title, ct => ExecuteMaintenance(item));
            if (!closing && maintenanceAssessment?.Suggested.Length > 0) ReviewMaintenanceRepairs();
        }, true);
        var repair = Actions(); Button(repair, "Revisar / escolher correções", ReviewMaintenanceRepairs, true);
        Button(repair, "Abrir logs CBS / DISM", () => OpenLink(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs")));
        var cleanup = Actions();
        Button(cleanup, "Prévia de temporários", () => _ = Run("Prévia de limpeza", async ct => { cleanupCandidates = await Task.Run(() => TempCleanup.Preview(), ct); maintenanceLog = $"{cleanupCandidates.Count} arquivos com mais de7dias · {cleanupCandidates.Sum(f => f.Bytes) / 1048576.0:N1} MiB\r\n" + string.Join("\r\n", cleanupCandidates.Take(500).Select(f => f.Path)); if (!log.IsDisposed) log.Text = maintenanceLog; return TestResult.Done("Prévia de temporários", ("Arquivos candidatos", cleanupCandidates.Count), ("MiB", cleanupCandidates.Sum(f => f.Bytes) / 1048576.0), ("Alterações", "Nenhuma")); }), true);
        Button(cleanup, "Limpar somente a prévia", () => {
            if (task is not null || cleanupCandidates.Count == 0) return;
            if (MessageBox.Show(this, "Excluir " + cleanupCandidates.Count + " arquivos temporários da prévia, com mais de7dias?\n\nArquivos em uso, recentes, links e itens alterados serão preservados. A exclusão não pode ser desfeita pelo app. Lixeira, Downloads, documentos e cache de atualizações ficam preservados.", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            var selected = cleanupCandidates.ToArray(); _ = Run("Limpeza de temporários", async ct => { var result = await Task.Run(() => TempCleanup.Apply(selected)); cleanupCandidates.Clear(); maintenanceLog = OperationResults.Summary(result); if (!log.IsDisposed) log.Text = maintenanceLog; return result; });
        }, true);
        var diagnostics = Actions();
        Button(diagnostics, "Segurança / serviços / inicialização", () => _ = Run("Diagnóstico do Windows", async ct => {
            maintenanceLog = "Consultando Defender, firewall, serviços, discos e inicialização…"; if (!log.IsDisposed) log.Text = maintenanceLog;
            var script = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$rows=@();try{$rows+= [pscustomobject]@{Grupo='Defender';Dados=(Get-MpComputerStatus|Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureLastUpdated)}}catch{$rows+=[pscustomobject]@{Grupo='Defender';Erro=$_.Exception.Message}};try{$rows+=[pscustomobject]@{Grupo='Firewall';Dados=@(Get-NetFirewallProfile|Select-Object Name,Enabled)}}catch{$rows+=[pscustomobject]@{Grupo='Firewall';Erro=$_.Exception.Message}};$rows+=[pscustomobject]@{Grupo='Serviços';Dados=@(Get-Service|Where-Object {$_.Name -in @('Spooler','wuauserv','bits','WinDefend','Dnscache','LanmanWorkstation','W32Time')}|Select-Object Name,Status,StartType)};try{$rows+=[pscustomobject]@{Grupo='Discos';Dados=@(Get-PhysicalDisk|Select-Object FriendlyName,MediaType,Size,HealthStatus,OperationalStatus)}}catch{$rows+=[pscustomobject]@{Grupo='Discos';Erro=$_.Exception.Message}};$rows+=[pscustomobject]@{Grupo='Inicialização';Dados=@(Get-CimInstance Win32_StartupCommand|Select-Object Name,Location)};ConvertTo-Json -InputObject $rows -Depth 7 -Compress";
            var outcome = await UpdateProcess(script, ct); if (outcome.ExitCode != 0) throw new IOException(outcome.Error);
            maintenanceLog = outcome.Output; if (!log.IsDisposed) log.Text = maintenanceLog;
            return TestResult.Done("Segurança / serviços / inicialização", ("Consulta", System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(outcome.Output)), ("Alterações", "Nenhuma"));
        }), true);
        Heading("Manutenção do Windows", "Andamento desde o início, saída ao vivo, diagnóstico e correções por etapa à sua escolha. SFC/DISM podem demorar e ficar sem emitir texto; o tempo continua visível. Reinício somente manual.");
    }
}
