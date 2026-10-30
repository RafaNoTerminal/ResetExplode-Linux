using System.Text.Json;
namespace Diagnostico.Windows;

internal sealed class ElginSetupDialog : Form
{
    readonly CheckedListBox queues = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true };
    readonly TextBox output = new() { Dock = DockStyle.Bottom, Height = 135, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly Label state = new() { Dock = DockStyle.Bottom, Height = 44, Text = "Instale o driver, marque as unidades e clique em Adicionar e configurar automaticamente. Nenhum modelo manual é necessário." };
    readonly List<Button> operations = [];
    readonly Action<TestResult> record; readonly bool queryOnShow;
    bool busy;
    internal ElginSetupDialog(CompanyPrinter[] printers, CompanyPrinter[] selected, Action<TestResult> recordResult, bool collectOnShow = true) {
        record = recordResult; queryOnShow = collectOnShow;
        Text = "Comprovantes Elgin i9 · driver, corte e logotipo"; Size = new Size(1040, 790); MinimumSize = new Size(900, 680); StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);
        foreach (var p in printers.Where(p => p.Receipt)) queues.Items.Add(p, selected.Any(s => s.Name == p.Name));
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var install = new TabPage("1 · Driver e filas");
        var intro = new Label { Dock = DockStyle.Top, Height = 92, Text = "Instale o driver no PC/notebook antes de adicionar as filas. O pacote fornece o modelo ELGIN i9(USB), usado também nas filas TCP/IP de comprovantes. O instalador original pode oferecer criar uma fila USB; preserve as filas que já existem. Depois, marque as unidades desejadas e adicione/configure automaticamente. Papel, corte parcial e seleção do logo são aplicados sem modelo manual." };
        var driverActions = Row(install, DockStyle.Top);
        Add(driverActions, "1 · Instalar driver Elgin 1.8.0", () => Install(false));
        Add(driverActions, "Atualizar inventário", () => _ = Operate(RefreshInventory));
        operations.AddRange(PrinterSelectionButtons.Add(driverActions, queues));
        var queueActions = Row(install, DockStyle.Bottom);
        Add(queueActions, "2 · Adicionar e configurar automaticamente", () => _ = Operate(async () => {
            var chosen = Selected();
            if (chosen.Length == 0) throw new IOException("Marque as unidades desejadas.");
            if (MessageBox.Show(this, "Adicionar somente as filas ausentes usando " + ElginSetupService.DriverName + "?\r\n\r\n" + string.Join("\r\n", chosen.Select(p => p.Name + " → " + p.Ip)) + "\r\n\r\nFilas existentes, portas, trabalhos e padrão serão preservados. Em seguida o app aplica papel 72×100 mm, corte parcial no fim da página/0 mm e logo salvo no início da página/Meio/chave 0/0, com backup. Não é preciso modelo manual.", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return null;
            if (!await EnsureDriver()) return null;
            using var before = await CompanyService.Script(PrinterScripts.Inventory, CancellationToken.None);
            Directory.CreateDirectory(Automation.BackupDirectory);
            string file = Path.Combine(Automation.BackupDirectory, "elgin-inventario-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            await File.WriteAllTextAsync(file, before.RootElement.GetRawText());
            var result = await CompanyService.ApplyPrinters(PrinterScripts.Install(chosen, ElginSetupService.DriverName), "Adicionar filas Elgin"); result.Dados["Inventário anterior"] = file;
            var configured = await ElginSetupService.ApplyAutomatic(chosen);
            var rows = OperationResults.Read(result).Concat(OperationResults.Read(configured)).ToArray();
            result = new("Instalar e configurar comprovantes", rows.Any(r => r.Failed) ? "Concluído com falhas" : "Filas e ajustes automáticos confirmados; teste físico pendente", new() { ["Etapas"] = rows, ["Inventário anterior"] = file }, DateTimeOffset.Now);
            await RefreshInventory(); return result;
        }));
        install.Controls.Add(queues); install.Controls.Add(driverActions); install.Controls.Add(intro); install.Controls.Add(queueActions);
        var configure = new TabPage("2 · Ajustes automáticos");
        var guide = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = "AJUSTE AUTOMÁTICO — SEM MODELO MANUAL\r\n\r\n" + ElginReceiptPreset.Description + "\r\n\r\nUse Aplicar automaticamente às filas marcadas para ajustar filas que já estão instaladas. Na primeira aba, Adicionar e configurar automaticamente faz as duas etapas em sequência.\r\n\r\nCompatível com o driver Elgin 1.8.0 x64 fornecido. O app confere os arquivos do driver, salva backup completo e relê todos os valores. Não é necessário abrir as preferências nem capturar um modelo.\r\n\r\nA imagem do logo precisa estar gravada na memória física de cada impressora. O app aplica a seleção do logo salvo; a gravação da imagem pelo utilitário é uma etapa separada, caso ainda não exista.\r\n\r\nFaça teste físico de um comprovante para conferir o resultado." };
        var modelActions = Row(configure, DockStyle.Bottom); modelActions.Height = 90; modelActions.WrapContents = true;
        Add(modelActions, "Aplicar automaticamente às filas marcadas", () => _ = Operate(ApplyAutomaticSelection));
        Add(modelActions, "Instalar utilitário Elgin", () => Install(true));
        Add(modelActions, "Logo e imagens de referência", () => { if (!busy) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ElginSetupService.Materials) { UseShellExecute = true }); });
        configure.Controls.Add(guide); configure.Controls.Add(modelActions);
        var apply = new TabPage("3 · Aplicar padrão e restaurar backup");
        var explanation = new Label { Dock = DockStyle.Top, Height = 140, Text = "Aplique diretamente o padrão de comprovantes às filas marcadas: papel 72×100 mm, corte parcial no fim da página/0 mm e logo salvo no início/Meio/chave 0/0. Não precisa de impressora de exemplo, captura ou arquivo de perfil.\r\n\r\nRestaurar backup é apenas para desfazer um ajuste anterior neste mesmo PC e fila. Nesse caso, escolha o backup salvo pelo RBT, sem criar modelo." };
        var applyActions = Row(apply, DockStyle.Top); applyActions.Height = 110; applyActions.WrapContents = true;
        Add(applyActions, "Aplicar padrão automático às filas marcadas", () => _ = Operate(ApplyAutomaticSelection));
        Add(applyActions, "Restaurar backup completo", () => _ = Operate(async () => {
            using var pick = new OpenFileDialog { Filter = "Backup completo de preferências|elgin-completo-*.json", InitialDirectory = Automation.BackupDirectory };
            if (pick.ShowDialog(this) != DialogResult.OK) return null;
            string file = pick.FileName; var backup = JsonSerializer.Deserialize<ReceiptQueueBackup>(File.ReadAllText(file)) ?? throw new IOException("Backup inválido.");
            if (MessageBox.Show(this, "Restaurar as preferências completas da fila " + backup.Queue + " neste PC?", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return null;
            await ElginSetupService.CheckQueue(backup.Queue);
            string undo = await Task.Run(() => { using var settings = new ElginPreferences(backup.Queue); string saved = settings.Backup(Automation.BackupDirectory); settings.Restore(file); return saved; });
            return TestResult.Done("Restaurar preferências completas Elgin", ("Fila", backup.Queue), ("Backup antes de restaurar", undo), ("Resultado", "Preferências restauradas e relidas; impressão física pendente."));
        }));
        apply.Controls.Add(applyActions); apply.Controls.Add(explanation);
        tabs.TabPages.AddRange([install, configure, apply]); Controls.Add(tabs); Controls.Add(output); Controls.Add(state);
        Shown += async (_, _) => { if (queryOnShow) await Operate(RefreshInventory); };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; state.Text = "Aguarde o instalador/operação concluir antes de fechar."; } };
    }
    static FlowLayoutPanel Row(Control parent, DockStyle dock) => new() { Dock = dock, Height = 52, WrapContents = false };
    void Add(FlowLayoutPanel row, string text, Action action) { var button = new Button { Text = text, AutoSize = true, Height = 40, Padding = new Padding(6) }; button.Click += (_, _) => action(); row.Controls.Add(button); operations.Add(button); }
    CompanyPrinter[] Selected() => queues.CheckedItems.Cast<CompanyPrinter>().ToArray();
    async Task Operate(Func<Task<TestResult?>> operation) {
        if (busy) return; busy = true; operations.ForEach(b => b.Enabled = false); state.Text = "Operação em andamento; aguarde…";
        try { var result = await operation(); if (result is not null) { record(result); output.Text = OperationResults.Summary(result); state.Text = result.Estado; } else state.Text = "Nenhuma alteração confirmada nesta ação."; }
        catch (Exception e) { output.Text = e.Message; state.Text = "Operação não concluída"; record(new("Comprovantes Elgin", "Não concluído", new() { ["Erro"] = e.Message }, DateTimeOffset.Now)); MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { busy = false; operations.ForEach(b => b.Enabled = true); }
    }
    async Task<bool> EnsureDriver() {
        async Task<bool> Present() { using var data = await CompanyService.Script(PrinterScripts.Inventory, CancellationToken.None); return data.RootElement.GetProperty("Drivers").EnumerateArray().Any(d => d.GetProperty("Name").GetString() == ElginSetupService.DriverName); }
        return await PrinterDriverRequirement.Ensure(Present, async () => {
            if (MessageBox.Show(this, "O driver Elgin ainda não está instalado. Deseja instalar agora antes de criar as filas?\r\n\r\nSerá aberto o instalador original Elgin 1.8.0 do pendrive, com SHA256 conferido. Esse EXE antigo não tem assinatura Authenticode. Siga o instalador até concluir, preservando as filas existentes. Se solicitar reinício, faça manualmente antes de continuar.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) return false;
            var result = await ElginSetupService.InstallOriginal(false); record(result); output.Text = OperationResults.Summary(result); await RefreshInventory();
            int code = Convert.ToInt32(result.Dados["Retorno"]);
            if (code == 3010) throw new IOException("O instalador Elgin pediu reinício. Nenhuma fila foi criada; reinicie manualmente e tente novamente.");
            if (code != 0) throw new IOException("Instalador Elgin retornou " + code + ". Nenhuma fila foi criada; confira o resultado do instalador.");
            return true;
        });
    }
    async Task<TestResult?> RefreshInventory() {
        using var data = await CompanyService.Script(PrinterScripts.Inventory, CancellationToken.None);
        int queueCount = data.RootElement.GetProperty("Printers").EnumerateArray().Count(p => p.GetProperty("DriverName").GetString() == ElginSetupService.DriverName);
        bool driver = data.RootElement.GetProperty("Drivers").EnumerateArray().Any(d => d.GetProperty("Name").GetString() == ElginSetupService.DriverName);
        return TestResult.Done("Inventário Elgin", ("Driver instalado", driver), ("Filas Elgin disponíveis", queueCount), ("Próximo passo", driver ? "Marque as filas e aplique os ajustes automáticos diretamente." : "Instale o driver Elgin antes de adicionar filas."));
    }
    void Install(bool utility) {
        if (busy) return;
        string file = utility ? "ELGIN_i9_Utility_Setup V1.2.2.31.exe" : "ELGIN Printer Driver_v-1.8.0 1.exe";
        if (MessageBox.Show(this, "Executar o instalador original fornecido no pendrive?\r\n\r\n" + file + "\r\n\r\nEste EXE antigo não tem assinatura Authenticode. O app confere o SHA256 exato do arquivo fornecido antes de abrir. Siga o instalador e preserve as impressoras existentes. Pode solicitar reinício; faça manualmente.\r\n\r\n" + (utility ? "Use o utilitário para gravar o logotipo na impressora correta; não é preciso atualizar firmware." : "Instale o modelo ELGIN i9(USB) no computador. Depois atualize o inventário e adicione as filas."), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        _ = Operate(async () => { var result = await ElginSetupService.InstallOriginal(utility); if (!utility) await RefreshInventory(); return result; });
    }
    async Task<TestResult?> ApplyAutomaticSelection() {
        var chosen = Selected();
        if (chosen.Length == 0) { ((TabControl)Controls.OfType<TabControl>().Single()).SelectedIndex = 0; throw new IOException("Marque as unidades desejadas na lista. Não é preciso escolher uma impressora modelo."); }
        if (MessageBox.Show(this, ElginReceiptPreset.Description + "\r\n\r\nAplicar diretamente com backup às filas marcadas? Não exige modelo nem perfil de exemplo.", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return null;
        return await ElginSetupService.ApplyAutomatic(chosen);
    }
    internal void ShowRestorePage() { ((TabControl)Controls.OfType<TabControl>().Single()).SelectedIndex = 2; Application.DoEvents(); }
    internal void VerifyUi() {
        if (operations.Count != 10) throw new Exception("Fluxo automático/restauração ou guia de corte/logo ausente.");
        if (!operations.Any(b => b.Text == "2 · Adicionar e configurar automaticamente") || !operations.Any(b => b.Text == "Aplicar automaticamente às filas marcadas")) throw new Exception("Ação automática sem modelo manual ausente.");
        ((TabControl)Controls.OfType<TabControl>().Single()).SelectedIndex = 1; Application.DoEvents();
        if (operations.Any(b => b.Text.Contains("Capturar") || b.Text.Contains("Importar perfil")) || !operations.Any(b => b.Text == "Aplicar padrão automático às filas marcadas") || busy) throw new Exception("Fixture iniciou consulta/instalador ou acessou driver real.");
    }
}
