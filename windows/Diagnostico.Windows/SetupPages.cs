using System.Diagnostics;

namespace Diagnostico.Windows;

public sealed partial class MainForm
{
    List<InfoRow> vivobook = [];
    TestResult? lastSetupResult;
    Button? lastSetupButton;
    void BuildAutomation(bool notebook = false)
    {
        var selected = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Surface, ForeColor = Ink, BorderStyle = BorderStyle.None, Font = new Font(Font.FontFamily, 11), IntegralHeight = false };
        var options = AutomationCatalog.All.Where(a => notebook ? a.Id is "usb-ac" or "pcie-ac" or "drivers-wu" : a.Id is not ("usb-ac" or "pcie-ac")).ToArray();
        selected.Items.AddRange(options);
        var description = new TextBox { Dock = DockStyle.Bottom, Height = 95, Multiline = true, ReadOnly = true, BackColor = Surface, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical, Text = "Nenhuma ação marcada. Clique em uma linha para ler escopo e efeitos. Remoção de apps exige reinstalação para desfazer." };
        selected.SelectedIndexChanged += (_, _) => { if (selected.SelectedItem is SetupAction action) description.Text = action.Title + "\r\nEscopo: " + action.Scope + "\r\n" + action.Description; };
        content.Controls.Add(selected); content.Controls.Add(description);
        var actions = Actions();
        SetupAction[] Current() => AutomationCatalog.Select(selected.CheckedItems.Cast<SetupAction>().Select(a => a.Id));
        Button(actions, "Ver prévia", () => _ = ReviewSetup(Current, false), true);
        Button(actions, "Revisar e aplicar", () => _ = ReviewSetup(Current, true), true);
        Button(actions, "Restaurar backup", RestoreSetup, true);
        var history = Actions(); lastSetupButton = Button(history, "Último resultado", () => { if (lastSetupResult is not null) { using var dialog = SetupResultDialog(lastSetupResult); dialog.ShowDialog(this); } }); lastSetupButton.Enabled = lastSetupResult is not null;
        Heading(notebook ? "Vivobook · ajustes de teste" : "Ajustes do Windows", "Marque apenas o que deseja aplicar. A prévia mostra as mudanças; registro e energia têm backup local antes da alteração. Perfil de destino: " + Environment.UserName + (Automation.Admin ? " · administrador" : " · usuário comum"));
    }
    async Task ReviewSetup(Func<SetupAction[]> getActions, bool apply)
    {
        if (task is not null) return;
        SetupAction[] actions;
        try { actions = getActions(); if (actions.Length == 0) { MessageBox.Show(this, "Marque ao menos uma ação.", Brand.Name); return; } }
        catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); return; }
        await Run("Prévia dos ajustes", async ct => {
            var preview = await Automation.Preview(actions, ct); ct.ThrowIfCancellationRequested();
            using var review = new Form { Text = apply ? "Conferir antes de aplicar" : "Prévia · nenhuma alteração", Size = new Size(1000, 700), MinimumSize = new Size(780, 500), StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = Ink, Font = Font };
            var table = new DataGridView { Dock = DockStyle.Fill, DataSource = preview, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, BackgroundColor = Surface };
            table.DefaultCellStyle.WrapMode = DataGridViewTriState.True; table.DefaultCellStyle.BackColor = Surface; table.DefaultCellStyle.ForeColor = Ink;
            var note = new Label { Text = "Confira apps e escopo. Backup restaura registro/energia/inicialização; apps removidos precisam de reinstalação. Nenhum reinício automático. Ao aplicar, aguarde a etapa de remoção concluir antes de fechar.", Dock = DockStyle.Top, Height = 75, Padding = new Padding(14) };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            var cancel = new Button { Text = apply ? "Cancelar" : "Fechar", DialogResult = DialogResult.Cancel, Width = 125, Height = 36 }; footer.Controls.Add(cancel); review.CancelButton = cancel;
            if (apply) footer.Controls.Add(new Button { Text = "Aplicar seleção", DialogResult = DialogResult.OK, Width = 155, Height = 36 });
            review.Controls.Add(table); review.Controls.Add(note); review.Controls.Add(footer); review.ActiveControl = cancel;
            bool confirm = review.ShowDialog(this) == DialogResult.OK;
            if (!apply || !confirm) return TestResult.Done("Prévia dos ajustes", ("Ações", actions.Select(a => a.Title).ToArray()), ("Mudanças aplicadas", "Nenhuma"));
            // Appx servicing/uninstall is allowed to finish its current transaction.
            stop.Enabled = false; status.Text = "Aplicando seleção · aguarde a etapa atual concluir…";
            var result = await Automation.Apply(actions, ct);
            lastSetupResult = result;
            if (lastSetupButton is { IsDisposed: false }) lastSetupButton.Enabled = true;
            using (var dialog = SetupResultDialog(result)) dialog.ShowDialog(this);
            return result;
        });
    }
    void RestoreSetup()
    {
        if (task is not null) return;
        using var dialog = new OpenFileDialog { Title = "Escolher backup de ajustes deste PC e perfil", InitialDirectory = Automation.BackupDirectory, Filter = "Backup de ajustes (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "Restaurar os valores anteriores de registro e energia deste backup?\nApps removidos não são reinstalados.\n\n" + dialog.FileName, Brand.Name, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        string file = dialog.FileName;
        _ = Run("Restaurar ajustes", async ct => { ct.ThrowIfCancellationRequested(); stop.Enabled = false; return await Automation.Restore(file); });
    }
    void BuildVivobook()
    {
        var grid = Grid(); grid.DataSource = vivobook.ToList();
        var actions = Actions();
        Button(actions, "Analisar vídeo / USB", () => _ = Run("Diagnóstico Vivobook", async ct => { vivobook = await Task.Run(Vivobook.Collect, ct); if (!grid.IsDisposed) grid.DataSource = vivobook.ToList(); return TestResult.Done("Diagnóstico Vivobook / ASUS", ("Dados e checklist", vivobook.ToArray())); }), true);
        Button(actions, "Ajustes de teste", () => { foreach (Control c in content.Controls.Cast<Control>().ToArray()) c.Dispose(); content.Controls.Clear(); runButtons.Clear(); BuildAutomation(notebook: true); });
        Button(actions, "Suporte ASUS", () => OpenLink("https://www.asus.com/support/download-center/"));
        var shortcuts = Actions();
        Button(shortcuts, "Dispositivos", () => OpenSystemTool("mmc.exe", "devmgmt.msc"));
        Button(shortcuts, "Configurar telas", () => OpenLink("ms-settings:display"));
        Button(shortcuts, "Windows Update", () => OpenLink("ms-settings:windowsupdate"));
        Button(shortcuts, "MyASUS · diagnóstico", () => OpenLink("https://www.asus.com/global/support/faq/1045716/"));
        var checklist = new Label { Dock = DockStyle.Top, Height = 100, Text = "Monitor piscando ou sumindo: 1) testar cabo/HDMI direta; 2) conferir modelo e driver ASUS; 3) comparar tela interna/externa; 4) testar tampa e bloqueio/desbloqueio. Ajustes USB/PCIe são opcionais, temporários e só na tomada. Eventos ajudam a investigar, não comprovam a causa.", ForeColor = Color.Silver }; content.Controls.Add(checklist);
        Heading("Vivobook / ASUS", "Reúne modelo/BIOS, driver de vídeo/rede/USB, telas detectadas e até 50 eventos das últimas 72 horas. Funciona como coleta geral em outros PCs também.");
    }
    void OpenLink(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); } }
    void OpenSystemTool(string executable, string argument) { try { var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, executable)) { UseShellExecute = true }; start.ArgumentList.Add(argument); Process.Start(start); } catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); } }
}
