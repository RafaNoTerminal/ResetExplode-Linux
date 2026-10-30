using System.ComponentModel;
using System.Text.Json;
namespace Diagnostico.Windows;

public sealed partial class MainForm
{
    OnboardingProfile onboarding = OnboardingProfile.Default;
    void BuildOnboarding() {
        try { onboarding = OnboardingService.Load(); } catch (Exception e) { status.Text = "Setores: " + e.Message; }
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Surface, ForeColor = Ink, IntegralHeight = false, HorizontalScrollbar = true }; content.Controls.Add(list);
        var details = new TextBox { Dock = DockStyle.Bottom, Height = 115, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Surface, ForeColor = Ink, Text = "Marque somente o que você já conferiu manualmente neste PC. A marcação é um checklist desta tela; abrir download não confirma instalação." }; content.Controls.Add(details);
        var top = Actions(); var sectors = new ComboBox { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList }; sectors.Items.AddRange(onboarding.Sectors.Cast<object>().ToArray()); top.Controls.Add(sectors);
        void Populate() { list.Items.Clear(); if (sectors.SelectedItem is OnboardingSector sector) list.Items.AddRange(sector.Items.Cast<object>().ToArray()); }
        sectors.SelectedIndexChanged += (_, _) => Populate(); sectors.SelectedIndex = 0;
        list.SelectedIndexChanged += (_, _) => { if (list.SelectedItem is OnboardingItem item) details.Text = item.Title + "\r\n" + item.Url + (item.PrinterName.Length > 0 ? "\r\nFila: " + item.PrinterName : "") + "\r\n" + item.Notes; };
        Button(top, "Editar setor", () => { if (task is null && sectors.SelectedItem is OnboardingSector sector) EditSector(sector); });
        Button(top, "Novo setor", () => { if (task is null) EditSector(null); });
        var actions = Actions();
        Button(actions, "Abrir download / página", () => {
            if (list.SelectedItem is not OnboardingItem item) return;
            try { if (item.Url.Length == 0) { MessageBox.Show(this, item.Notes, Brand.Name); return; } OpenLink(OnboardingProfile.WebUri(item.Url).AbsoluteUri); }
            catch (Exception e) { MessageBox.Show(this, e.Message, Brand.Name); }
        });
        Button(actions, "Criar atalho Chrome", () => {
            if (task is not null || list.SelectedItem is not OnboardingItem item || item.Kind != OnboardingKind.ChromeApp) { MessageBox.Show(this, "Cadastre e selecione um item do tipo ChromeApp com a URL do setor.", Brand.Name); return; }
            if (MessageBox.Show(this, "Criar atalho em janela Chrome na área de trabalho de todos os usuários?\n\n" + item.Title + "\n" + item.Url + "\n\nO login será feito pelo colaborador. A instalação como PWA depende do site e do Chrome.", Brand.Name, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            _ = Run("Atalho Chrome", async ct => { var output = await UpdateProcess(OnboardingService.ChromeShortcut(item), ct); if (output.ExitCode != 0) throw new IOException(output.Error); return TestResult.Done("Atalho Chrome", ("Item", item.Title), ("Resultado", output.Output)); });
        }, true);
        Button(actions, "Ver impressora do setor", () => {
            if (list.SelectedItem is not OnboardingItem item || item.Kind != OnboardingKind.Printer) { MessageBox.Show(this, "Cadastre um item do tipo Printer com o nome exato da fila.", Brand.Name); return; }
            menu.SelectedIndex = 13;
            var printers = content.Controls.OfType<CheckedListBox>().Single();
            for (int i = 0; i < printers.Items.Count; i++) if (printers.Items[i] is CompanyPrinter p && p.Name.Equals(item.PrinterName, StringComparison.OrdinalIgnoreCase)) { printers.SelectedIndex = i; printers.SetItemChecked(i, true); return; }
            MessageBox.Show(this, "Fila não está no catálogo local: " + item.PrinterName + ". Confira com o setor antes de instalar.", Brand.Name);
        });
        var report = Actions();
        Button(report, "Registrar checklist no relatório", () => {
            if (sectors.SelectedItem is not OnboardingSector sector) return;
            var rows = sector.Items.Select((item, i) => new OperationRow("Conferência manual", item.Title, sector.Name, list.GetItemChecked(i) ? "Conferido pelo técnico" : "Pendente", item.Notes)).ToArray();
            results.Add(TestResult.Done("Preparação por setor", ("Setor", sector.Name), ("Etapas", rows), ("Limite", "Marcações manuais nesta tela; não é detecção automática de instalação."))); status.Text = "Checklist incluído no relatório da sessão.";
        });
        Heading("Central Helpdesk · novos colaboradores", "Preparação por setor: aplicativos Windows, páginas em janela Chrome e impressoras. Suporte baseado nos oito apps do técnico interno do Mint. Defina os próximos setores com cada equipe. Marcas = conferência manual nesta tela.");
    }
    void EditSector(OnboardingSector? original) {
        using var dialog = new Form { Text = "Configurar preparação do setor", Size = new Size(1050, 650), StartPosition = FormStartPosition.CenterParent };
        var name = new TextBox { Dock = DockStyle.Top, Text = original?.Name ?? "Novo setor", Height = 32 };
        var items = JsonSerializer.Deserialize<List<OnboardingItem>>(JsonSerializer.Serialize(original?.Items ?? []))!;
        var source = new BindingList<OnboardingItem>(items);
        var grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, DataSource = source, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Title", HeaderText = "Aplicativo / item" });
        grid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName = "Kind", HeaderText = "Tipo", DataSource = Enum.GetValues<OnboardingKind>() });
        foreach (var (field, title) in new[] { ("Url", "URL HTTPS"), ("PrinterName", "Nome exato da fila"), ("Notes", "Orientações (sem senhas)") }) grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = field, HeaderText = title });
        var help = new Label { Dock = DockStyle.Bottom, Height = 60, Text = "Download: página oficial para obter o app. ChromeApp: atalho em janela Chrome. Printer: nome de fila do catálogo de impressoras. Checklist: tarefa manual. Não coloque senhas, tokens ou dados de login." };
        var save = new Button { Dock = DockStyle.Bottom, Height = 42, Text = "Salvar setor" };
        save.Click += (_, _) => {
            try {
                grid.EndEdit(); dialog.BindingContext?[source]?.EndCurrentEdit();
                var sector = new OnboardingSector(name.Text.Trim(), source.Where(i => !string.IsNullOrWhiteSpace(i.Title)).ToList());
                var updated = new OnboardingProfile(onboarding.Sectors.Where(s => s != original).Append(sector).ToList());
                OnboardingService.Save(updated); onboarding = updated; dialog.DialogResult = DialogResult.OK;
            } catch (Exception e) { MessageBox.Show(dialog, e.Message, Brand.Name); }
        };
        dialog.Controls.Add(grid); dialog.Controls.Add(name); dialog.Controls.Add(help); dialog.Controls.Add(save);
        if (dialog.ShowDialog(this) == DialogResult.OK) ShowPage(15);
    }
}
