using System.Text.Json;
namespace Diagnostico.Windows;

internal sealed class ElginSetupDialog : Form
{
    internal const string Guide = "PAPEL\r\nCrie/selecione o papel Comprovantes: largura 72,0 mm e comprimento 100,0 mm.\r\n\r\nCONFIGURAÇÕES AVANÇADAS → PAPEL CORTADO\r\nTempo: Fim da página.\r\nModo: Papel parcialmente cortado.\r\nDistância: 0,0 mm.\r\n\r\nCONFIGURAÇÕES AVANÇADAS → IMPRESSÃO DO LOGOTIPO\r\nTempo: Início da página.\r\nEscolha: Logo salvo com impressora.\r\nLocalização: Meio (centralizado).\r\nCódigo chave: 0 / 0 (48 / 48 aparece acima na referência).\r\nConfirme OK em todas as janelas.\r\n\r\nO logotipo deve estar gravado na memória de CADA impressora física. O perfil copia as preferências do driver; não grava a imagem na impressora. Use o utilitário Elgin com o arquivo logo novo rbt.png, sem atualizar firmware.\r\n\r\nO modelo será aplicado como padrão global (novos usuários) e na conta que executa o RBT. Se o administrador for outra conta, preferências já personalizadas do colaborador precisam de conferência no perfil dele.\r\n\r\nCapture uma vez e reaplique nas filas/PCs com o mesmo driver. Faça teste físico de um comprovante para conferir corte e logotipo.";
    readonly CheckedListBox queues = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true };
    readonly ComboBox sourceQueue = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    readonly TextBox output = new() { Dock = DockStyle.Bottom, Height = 135, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly Label state = new() { Dock = DockStyle.Bottom, Height = 44, Text = "1. Instale o driver → 2. Adicione as filas → 3. Configure/capture um modelo → 4. Aplique o perfil completo." };
    readonly List<Button> operations = [];
    readonly Action<TestResult> record; readonly bool queryOnShow;
    bool busy; ReceiptProfile? profile;
    internal ElginSetupDialog(CompanyPrinter[] printers, CompanyPrinter[] selected, Action<TestResult> recordResult, bool collectOnShow = true) {
        record = recordResult; queryOnShow = collectOnShow;
        Text = "Comprovantes Elgin i9 · driver, corte e logotipo"; Size = new Size(1040, 790); MinimumSize = new Size(900, 680); StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);
        foreach (var p in printers.Where(p => p.Receipt)) queues.Items.Add(p, selected.Any(s => s.Name == p.Name));
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var install = new TabPage("1 · Driver e filas");
        var intro = new Label { Dock = DockStyle.Top, Height = 92, Text = "Instale o driver no PC/notebook antes de adicionar as filas. O pacote fornece o modelo ELGIN i9(USB), usado também nas filas TCP/IP de comprovantes. O instalador original pode oferecer criar uma fila USB; preserve as filas que já existem. Depois, marque apenas as unidades desejadas abaixo." };
        var driverActions = Row(install, DockStyle.Top);
        Add(driverActions, "1 · Instalar driver Elgin 1.8.0", () => Install(false));
        Add(driverActions, "Atualizar inventário", () => _ = Operate(RefreshInventory));
        var queueActions = Row(install, DockStyle.Bottom);
        Add(queueActions, "2 · Adicionar filas marcadas", () => _ = Operate(async () => {
            var chosen = Selected();
            if (chosen.Length == 0) throw new IOException("Marque as unidades desejadas.");
            if (MessageBox.Show(this, "Adicionar somente as filas ausentes usando " + ElginSetupService.DriverName + "?\r\n\r\n" + string.Join("\r\n", chosen.Select(p => p.Name + " → " + p.Ip)) + "\r\n\r\nFilas existentes, portas, trabalhos e padrão serão preservados. Depois configure/capture o modelo e aplique o perfil completo.", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return null;
            using var before = await CompanyService.Script(PrinterScripts.Inventory, CancellationToken.None);
            Directory.CreateDirectory(Automation.BackupDirectory);
            string file = Path.Combine(Automation.BackupDirectory, "elgin-inventario-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            await File.WriteAllTextAsync(file, before.RootElement.GetRawText());
            var result = await CompanyService.ApplyPrinters(PrinterScripts.Install(chosen, ElginSetupService.DriverName), "Adicionar filas Elgin"); result.Dados["Inventário anterior"] = file;
            await RefreshInventory(); return result;
        }));
        install.Controls.Add(queues); install.Controls.Add(driverActions); install.Controls.Add(intro); install.Controls.Add(queueActions);
        var configure = new TabPage("2 · Configurar modelo");
        var guide = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = Guide };
        var modelActions = Row(configure, DockStyle.Bottom); modelActions.Height = 90; modelActions.WrapContents = true;
        modelActions.Controls.Add(sourceQueue);
        Add(modelActions, "3 · Configurar e capturar modelo", () => _ = Operate(CaptureModel));
        Add(modelActions, "Instalar utilitário Elgin", () => Install(true));
        Add(modelActions, "Logo e imagens de referência", () => { if (!busy) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ElginSetupService.Materials) { UseShellExecute = true }); });
        configure.Controls.Add(guide); configure.Controls.Add(modelActions);
        var apply = new TabPage("3 · Aplicar / restaurar");
        var explanation = new Label { Dock = DockStyle.Top, Height = 140, Text = "Depois de configurar uma fila modelo conforme o guia, capture o perfil completo. O perfil inclui preferências próprias do driver, como corte e logotipo; permanece no pendrive em Empresa e pode ser reutilizado em PCs com os mesmos arquivos de driver.\r\n\r\nA aplicação usa as filas marcadas na primeira aba, cria backup completo e confere a releitura. Filas com driver/destino diferente ou trabalhos pendentes são bloqueadas. Falha ao aplicar tenta restaurar as preferências anteriores.\r\n\r\nA imagem física do logo não é transferida pelo perfil. Confira um comprovante em cada impressora." };
        var applyActions = Row(apply, DockStyle.Top); applyActions.Height = 105; applyActions.WrapContents = true;
        Add(applyActions, "4 · Aplicar perfil completo", () => _ = Operate(async () => {
            profile ??= ElginSetupService.LoadProfile(); var chosen = Selected(); if (chosen.Length == 0) throw new IOException("Marque filas na aba Driver e filas.");
            if (MessageBox.Show(this, "Aplicar o modelo completo de papel/corte/logotipo nas filas marcadas?\r\n\r\n" + string.Join("\r\n", chosen.Select(p => p.Name)) + "\r\n\r\nSalva backup, mantém destino/padrão e confere os dados do driver. Teste de impressão continua necessário.", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return null;
            return await ElginSetupService.Apply(profile, chosen);
        }));
        Add(applyActions, "Importar perfil de outro PC", () => _ = Operate(() => {
            using var pick = new OpenFileDialog { Filter = "Perfil completo RBT|*.rbt-profile.json", InitialDirectory = Path.GetDirectoryName(ElginSetupService.ProfileFile) };
            if (pick.ShowDialog(this) != DialogResult.OK) return Task.FromResult<TestResult?>(null);
            profile = ElginSetupService.LoadProfile(pick.FileName); ElginSetupService.SaveProfile(profile);
            return Task.FromResult<TestResult?>(TestResult.Done("Importar perfil completo", ("Resultado", "Perfil carregado; nenhuma fila alterada. Compatibilidade será conferida ao aplicar.")));
        }));
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
    async Task<TestResult?> RefreshInventory() {
        using var data = await CompanyService.Script(PrinterScripts.Inventory, CancellationToken.None);
        string? selected = sourceQueue.SelectedItem as string; sourceQueue.Items.Clear();
        foreach (var p in data.RootElement.GetProperty("Printers").EnumerateArray()) if (p.GetProperty("DriverName").GetString() == ElginSetupService.DriverName) sourceQueue.Items.Add(p.GetProperty("Name").GetString()!);
        if (sourceQueue.Items.Count > 0) sourceQueue.SelectedIndex = selected is not null && sourceQueue.Items.Contains(selected) ? sourceQueue.Items.IndexOf(selected) : 0;
        bool driver = data.RootElement.GetProperty("Drivers").EnumerateArray().Any(d => d.GetProperty("Name").GetString() == ElginSetupService.DriverName);
        return TestResult.Done("Inventário Elgin", ("Driver instalado", driver), ("Filas Elgin disponíveis", sourceQueue.Items.Count), ("Próximo passo", driver ? "Adicione as filas desejadas ou configure uma fila modelo." : "Instale o driver Elgin antes de adicionar filas."));
    }
    void Install(bool utility) {
        if (busy) return;
        string file = utility ? "ELGIN_i9_Utility_Setup V1.2.2.31.exe" : "ELGIN Printer Driver_v-1.8.0 1.exe";
        if (MessageBox.Show(this, "Executar o instalador original fornecido no pendrive?\r\n\r\n" + file + "\r\n\r\nEste EXE antigo não tem assinatura Authenticode. O app confere o SHA256 exato do arquivo fornecido antes de abrir. Siga o instalador e preserve as impressoras existentes. Pode solicitar reinício; faça manualmente.\r\n\r\n" + (utility ? "Use o utilitário para gravar o logotipo na impressora correta; não é preciso atualizar firmware." : "Instale o modelo ELGIN i9(USB) no computador. Depois atualize o inventário e adicione as filas."), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        _ = Operate(async () => { var result = await ElginSetupService.InstallOriginal(utility); if (!utility) await RefreshInventory(); return result; });
    }
    async Task<TestResult?> CaptureModel() {
        if (sourceQueue.SelectedItem is not string name) throw new IOException("Atualize o inventário e escolha uma fila Elgin instalada para servir de modelo.");
        await ElginSetupService.CheckQueue(name);
        using var native = new ElginPreferences(name);
        string backup = native.Backup(Automation.BackupDirectory);
        var mode = native.Edit(Handle); if (mode is null) return null;
        if (MessageBox.Show(this, "Você conferiu nesta fila os ajustes do guia?\r\n\r\n• Papel Comprovantes 72×100 mm\r\n• Fim da página / corte parcial / 0 mm\r\n• Início da página / logo salvo / Meio / chave 0 e 0\r\n\r\nO app captura os dados completos do driver, mas não interpreta os campos proprietários visualmente. Só confirme após conferir as opções.\r\n\r\nSalvar modelo e aplicar estas preferências à fila " + name + "?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return null;
        profile = new(1, native.Driver, native.Fingerprint, mode, true, DateTimeOffset.Now);
        await ElginSetupService.CheckQueue(name);
        string appliedBackup = native.Apply(profile, Automation.BackupDirectory); ElginSetupService.SaveProfile(profile);
        return TestResult.Done("Capturar modelo completo Elgin", ("Fila modelo", name), ("Perfil salvo", ElginSetupService.ProfileFile), ("Backup anterior à janela", backup), ("Backup anterior à aplicação", appliedBackup), ("Conferência", "Ajustes do guia declarados conferidos pelo técnico. Preferências completas aplicadas e relidas. Teste físico e logo na memória da impressora ainda precisam de conferência."));
    }
    internal void VerifyUi() {
        if (operations.Count != 9 || !Guide.Contains("Papel parcialmente cortado") || !Guide.Contains("Logo salvo com impressora") || !Guide.Contains("0 / 0")) throw new Exception("Fluxo driver/modelo/perfil ou guia de corte/logo ausente.");
        if (sourceQueue.Items.Count != 0 || busy) throw new Exception("Fixture iniciou consulta/instalador ou acessou driver real.");
    }
}
