namespace Diagnostico.Windows;

public sealed partial class MainForm
{
    Form SetupResultDialog(TestResult result)
    {
        var rows = OperationResults.Read(result);
        var dialog = new Form { Text = "Resultado dos ajustes · " + result.Estado, Size = new Size(1100, 760), MinimumSize = new Size(850, 600), StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = Ink, Font = Font };
        int failures = rows.Count(r => r.Failed);
        var summary = new Label { Dock = DockStyle.Top, Height = 78, Padding = new Padding(14), Text = $"{failures} falha(s) · {rows.Count(r => r.Estado == "Removido")} app(s) removido(s) · {rows.Count(r => r.Estado == "Preservado")} protegido(s) preservado(s) · {rows.Count(r => r.Estado == "Ausente")} já ausente(s).\nSelecione uma linha para ler a mensagem completa do Windows abaixo." };
        var filter = new ComboBox { Name = "setupResultFilter", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Surface, ForeColor = Ink };
        filter.Items.AddRange(["Todos os resultados", "Somente falhas", "Apps removidos", "Componentes preservados"]); filter.SelectedIndex = failures > 0 ? 1 : 0;
        var detail = new TextBox { Name = "setupResultDetail", Dock = DockStyle.Bottom, Height = 180, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Surface, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
        var table = new DataGridView { Name = "setupResultGrid", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Surface, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, EnableHeadersVisualStyles = false };
        table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; table.ColumnHeadersHeight = 44;
        table.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(43, 52, 64), ForeColor = Ink, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(5) };
        table.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Ink, SelectionBackColor = Color.FromArgb(48, 83, 91), SelectionForeColor = Ink, Padding = new Padding(5) };
        table.RowTemplate.Height = 38; table.GridColor = Color.FromArgb(57, 65, 77);
        void Details() { detail.Text = table.CurrentRow?.DataBoundItem is OperationRow row ? row.Acao + "\r\n" + row.Item + "\r\nEscopo: " + row.Escopo + "\r\nResultado: " + row.Estado + "\r\n\r\n" + row.Detalhes : "Nenhum item neste filtro. Escolha Todos os resultados para conferir a execução completa."; }
        void Render() {
            table.DataSource = rows.Where(r => filter.SelectedIndex switch { 1 => r.Failed, 2 => r.Estado == "Removido", 3 => r.Estado == "Preservado", _ => true }).ToList();
            if (table.Columns["Acao"] is { } actionColumn) actionColumn.HeaderText = "Ação";
            if (table.Columns["Item"] is { } itemColumn) itemColumn.HeaderText = "App / ajuste";
            if (table.Columns["Detalhes"] is { } detailColumn) detailColumn.HeaderText = "Motivo / detalhes";
            if (table.Rows.Count > 0) table.CurrentCell = table.Rows[0].Cells[0];
            Details();
        }
        table.SelectionChanged += (_, _) => Details(); filter.SelectedIndexChanged += (_, _) => Render();
        table.CellFormatting += (_, e) => { if (e.RowIndex >= 0 && table.Rows[e.RowIndex].DataBoundItem is OperationRow row && e.CellStyle is not null) e.CellStyle.ForeColor = row.Failed ? Color.FromArgb(255, 152, 152) : row.Estado == "Preservado" ? Color.FromArgb(255, 211, 129) : Ink; };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var close = new Button { Text = "Fechar", Width = 120, Height = 34, DialogResult = DialogResult.OK }; footer.Controls.Add(close); dialog.CancelButton = close;
        dialog.Controls.Add(table); dialog.Controls.Add(filter); dialog.Controls.Add(summary); dialog.Controls.Add(detail); dialog.Controls.Add(footer);
        dialog.Shown += (_, _) => Render();
        return dialog;
    }

    internal void VerifyTaskAndFailureUi(string? screenshotBase = null)
    {
        void Capture(Control control, string suffix) { if (screenshotBase is null) return; using var image = new Bitmap(control.Width, control.Height); control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size)); image.Save(Path.Combine(Path.GetDirectoryName(screenshotBase)!, Path.GetFileNameWithoutExtension(screenshotBase) + suffix + ".png")); }
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Pump(Task operation) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!operation.IsCompleted && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(5); } Check(operation.IsCompleted, "A tarefa de teste não terminou."); operation.GetAwaiter().GetResult(); Application.DoEvents(); }
        Check(!stop.Visible, "Parar teste apareceu sem teste ativo.");
        var finish = new TaskCompletionSource<TestResult>();
        var running = Run("Fixture de teste", _ => finish.Task, cancellableTest: true);
        Check(stop.Visible && stop.Enabled, "Parar teste não apareceu durante teste cancelável.");
        finish.SetResult(TestResult.Done("Fixture")); Pump(running); Check(!stop.Visible, "Parar teste permaneceu depois da conclusão.");
        running = Run("Fixture de cancelamento", async ct => { await Task.Delay(Timeout.Infinite, ct); return TestResult.Done("Fixture"); }, cancellableTest: true);
        stop.PerformClick(); Pump(running); Check(!stop.Visible, "Parar teste permaneceu após cancelamento.");
        finish = new TaskCompletionSource<TestResult>(); running = Run("Fixture de consulta", _ => finish.Task);
        Check(!stop.Visible, "Parar teste apareceu durante consulta sem teste."); finish.SetResult(TestResult.Done("Fixture")); Pump(running);
        var fixture = new TestResult("Ajustes do Windows", "Concluído com falhas", new() { ["Etapas"] = new[] {
            new OperationRow("Simplificar barra", "HKCU\\Teste\\ShellFeedsTaskbarViewMode", "Perfil atual", "Erro", "Attempted to perform an unauthorized operation. HRESULT 0x80070005"),
            new OperationRow("Remover app", "Microsoft.GetHelp_fixture", "Perfil atual", "Erro", "Remoção bloqueada. HRESULT 0x80073CFA"),
            new OperationRow("Remover app", "Microsoft.XboxGameCallableUI_fixture", "Perfil atual", "Preservado", "Componente protegido do Windows."),
            new OperationRow("Remover app", "Microsoft.GamingApp", "Perfil atual", "Ausente", ""),
            new OperationRow("Remover app", "Microsoft.BingNews_fixture", "Perfil atual", "Removido", "Removido deste perfil.") } }, DateTimeOffset.Now);
        using (var dialog = SetupResultDialog(fixture)) {
            dialog.Show(this); Application.DoEvents();
            var grid = (DataGridView)dialog.Controls.Find("setupResultGrid", true).Single();
            var filter = (ComboBox)dialog.Controls.Find("setupResultFilter", true).Single();
            var details = (TextBox)dialog.Controls.Find("setupResultDetail", true).Single();
            Check(filter.SelectedIndex == 1 && grid.Rows.Count == 2, "A janela não destacou as duas falhas.");
            Check(details.Text.Contains("ShellFeedsTaskbarViewMode") && details.Text.Contains("0x80070005"), "Detalhe completo da falha ausente.");
            Capture(dialog, "-falhas");
            filter.SelectedIndex = 2; Application.DoEvents(); Check(grid.Rows.Count == 1 && ((OperationRow)grid.Rows[0].DataBoundItem!).Item == "Microsoft.BingNews_fixture", "Não mostrou qual app foi removido.");
            Capture(dialog, "-removidos");
            filter.SelectedIndex = 0; Application.DoEvents(); Check(grid.Rows.Count == 5, "Itens protegidos/ausentes não ficaram disponíveis no resultado completo.");
            dialog.Close();
        }
        var savedInventory = appInventory;
        appInventory = [new("App", "Microsoft.GetHelp_fixture", "Perfil atual", "Removido", ""), new("App", "Microsoft.GamingApp_fixture", "Perfil atual", "Erro", "Bloqueado"), new("App", "Microsoft.XboxGameCallableUI_fixture", "Perfil atual", "Preservado", "Protegido")];
        menu.SelectedIndex = 6; Application.DoEvents();
        var choices = content.Controls.OfType<CheckedListBox>().Single();
        Check(!choices.Items.Cast<SetupAction>().Any(a => a.Id == "app:Microsoft.GetHelp"), "App removido continuou no checklist.");
        Check(choices.Items.Cast<SetupAction>().Any(a => a.Id == "app:Microsoft.GamingApp") && choices.Items.Cast<SetupAction>().Any(a => a.Id == "app:Microsoft.Xbox*"), "Falhas ou protegidos sumiram do checklist.");
        Capture(this, "-lista");
        appInventory = savedInventory; menu.SelectedIndex = 0;
        results.Clear(); status.Text = "Pronto · os testes começam somente pelos botões."; Check(task is null && !stop.Visible, "Teste de UI deixou tarefa ou botão ativo.");
    }
}
