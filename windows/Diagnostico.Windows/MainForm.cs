using System.Diagnostics;
using System.Text;

namespace Diagnostico.Windows;

public sealed partial class MainForm : Form
{
    static readonly Color Background = Color.FromArgb(26, 29, 35), Surface = Color.FromArgb(36, 41, 49), Accent = Color.FromArgb(64, 199, 187), Ink = Color.FromArgb(232, 237, 244);
    readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 20) };
    readonly ListBox menu = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ItemHeight = 42, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 40, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 12, 0), Text = "Pronto · os testes começam somente pelos botões." };
    readonly Button stop = new() { Text = "Parar teste", Dock = DockStyle.Right, Width = 132, Enabled = false, Visible = false };
    readonly List<TestResult> results = [];
    readonly List<Button> runButtons = [];
    DeviceProfile device = DeviceProfile.Detect("", "", 0, []);
    List<InfoRow> inventory = [];
    CancellationTokenSource? task;
    bool closing;
    DataGridView? inventoryGrid, processGrid, resultsGrid;
    Label? metrics;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    readonly bool automaticQueries;
    readonly string[] pages = ["Visão geral", "Hardware e sistema", "Processos e memória", "Testes de desempenho", "Rede e internet", "Tela e periféricos", "Ajustes do Windows", "Vivobook / ASUS", "Resultados e relatório", "Sobre", "Atualizações", "Ferramentas / ativação", "Sensores", "Impressoras / empresa", "Manutenção Windows", "Central Helpdesk"];

    public MainForm(bool collectOnShow = true)
    {
        automaticQueries = collectOnShow;
        Text = Brand.Name + " · Windows"; Size = new Size(1180, 810); MinimumSize = new Size(970, 670);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Background; ForeColor = Ink; Font = new Font("Segoe UI", 10);
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 232, BackColor = Surface, Padding = new Padding(14, 20, 14, 14) };
        var brand = new Label { Text = Brand.Name + "\nWINDOWS  ·  " + Brand.Version, Dock = DockStyle.Top, Height = 100, Font = new Font("Segoe UI", 17, FontStyle.Bold), ForeColor = Accent };
        var footer = new Label { Text = "FERRAMENTAS DE TI\nPortátil · relatório local", Dock = DockStyle.Bottom, Height = 64, ForeColor = Color.Silver };
        menu.BackColor = Surface; menu.ForeColor = Ink; menu.Items.AddRange(pages);
        menu.DrawItem += (_, e) => { if (e.Index < 0) return; bool selected = (e.State & DrawItemState.Selected) != 0; using var brush = new SolidBrush(selected ? Color.FromArgb(47, 72, 79) : Surface); e.Graphics.FillRectangle(brush, e.Bounds); TextRenderer.DrawText(e.Graphics, pages[e.Index], Font, new Point(e.Bounds.X + 10, e.Bounds.Y + 11), selected ? Accent : Ink); };
        menu.SelectedIndexChanged += (_, _) => ShowPage(menu.SelectedIndex);
        sidebar.Controls.Add(menu); sidebar.Controls.Add(brand); sidebar.Controls.Add(footer);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = Surface }; bottom.Controls.Add(stop); bottom.Controls.Add(status); status.Dock = DockStyle.Fill;
        stop.Click += (_, _) => { task?.Cancel(); stop.Enabled = false; stop.Visible = false; status.Text = "Interrompendo a tarefa…"; };
        Controls.Add(content); Controls.Add(bottom); Controls.Add(sidebar);
        timer.Tick += (_, _) => { UpdateMemory(); if (menu.SelectedIndex == 2 && task is null) RefreshProcesses(); };
        Shown += async (_, _) => { timer.Start(); if (collectOnShow) await LoadInventory(); };
        FormClosing += (_, e) => { if (task is not null) { e.Cancel = true; closing = true; task.Cancel(); status.Text = "Encerrando o teste antes de fechar…"; } };
        FormClosed += (_, _) => { timer.Dispose(); sensorTimer.Dispose(); sensorMonitor.Dispose(); };
        menu.SelectedIndex = 0;
    }
    Label Heading(string title, string description) {
        var head = new Label { Text = title, Dock = DockStyle.Top, Height = 48, Font = new Font(Font.FontFamily, 24, FontStyle.Bold) };
        var sub = new Label { Text = description, Dock = DockStyle.Top, Height = 65, ForeColor = Color.FromArgb(170, 183, 200) };
        content.Controls.Add(sub); content.Controls.Add(head); return sub;
    }
    FlowLayoutPanel Actions() { var flow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, AutoSize = false, WrapContents = false }; content.Controls.Add(flow); return flow; }
    Button Button(FlowLayoutPanel flow, string text, Action action, bool workload = false) {
        var button = new Button { Text = text, AutoSize = true, Height = 38, MinimumSize = new Size(100, 38), FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Ink, Padding = new Padding(10, 0, 10, 0), Margin = new Padding(0, 0, 10, 8) };
        button.FlatAppearance.BorderColor = Color.FromArgb(75, 87, 103); button.Click += (_, _) => action(); flow.Controls.Add(button);
        if (workload) { runButtons.Add(button); button.Enabled = task is null; } return button;
    }
    DataGridView Grid() {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, BackgroundColor = Background, BorderStyle = BorderStyle.None, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, EnableHeadersVisualStyles = false };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Ink, SelectionBackColor = Color.FromArgb(48, 83, 91), SelectionForeColor = Ink, Padding = new Padding(6), WrapMode = DataGridViewTriState.True };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(43, 52, 64), ForeColor = Ink, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(6) };
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; grid.ColumnHeadersHeight = 44;
        grid.GridColor = Color.FromArgb(57, 65, 77); content.Controls.Add(grid); return grid;
    }
    void ShowPage(int index)
    {
        foreach (Control control in content.Controls.Cast<Control>().ToArray()) control.Dispose();
        content.Controls.Clear(); runButtons.Clear(); inventoryGrid = processGrid = resultsGrid = null; metrics = null; refreshAppChoices = null;
        switch (index) {
            case 0:
                var overview = Grid(); overview.DataSource = inventory.Where(x => x.Grupo is "Sistema" or "Equipamento" or "CPU").ToList();
                var actions = Actions(); Button(actions, "Atualizar inventário", () => _ = LoadInventory(), true); Button(actions, "Quem usa a RAM?", () => menu.SelectedIndex = 2); Button(actions, "Testar este PC", () => menu.SelectedIndex = 3);
                metrics = new Label { Dock = DockStyle.Top, Height = 70, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = Accent }; content.Controls.Add(metrics); UpdateMemory();
                Heading("Visão geral", "Identifique o equipamento, confira os recursos disponíveis e escolha os testes. Nenhuma carga é iniciada ao abrir."); break;
            case 1:
                inventoryGrid = Grid(); inventoryGrid.DataSource = inventory.ToList(); var hardware = Actions(); Button(hardware, "Atualizar inventário", () => _ = LoadInventory(), true);
                Heading("Hardware e sistema", "Dados locais do Windows: CPU, módulos de RAM, vídeo, discos, bateria, rede e dispositivos com erro."); break;
            case 2:
                processGrid = Grid(); RefreshProcesses(); var processes = Actions(); Button(processes, "Atualizar processos", RefreshProcesses);
                metrics = new Label { Dock = DockStyle.Top, Height = 52, ForeColor = Accent }; content.Controls.Add(metrics); UpdateMemory();
                Heading("Processos e memória", "Maiores consumidores de RAM primeiro. Atualização a cada 4 s nesta página. Working set pode incluir páginas compartilhadas."); break;
            case 3: BuildTests(); break;
            case 4: BuildNetwork(); break;
            case 5: BuildPeripherals(); break;
            case 6: BuildAutomation(); break;
            case 7: BuildVivobookHistory(); break;
            case 10: BuildUpdates(); break;
            case 11: BuildTools(); break;
            case 12: BuildSensors(); break;
            case 13: BuildCompanyPrinters(); break;
            case 14: BuildMaintenance(); break;
            case 15: BuildHelpdesk(); break;
            case 8:
                resultsGrid = Grid(); RefreshResults(); var export = Actions(); Button(export, "Salvar HTML + JSON", Export); Button(export, "Atualizar lista", RefreshResults);
                Heading("Resultados e relatório", "Os resultados ficam nesta sessão até você salvar. O relatório inclui identificação do PC e endereços da rede local; não é enviado a um servidor."); break;
            default:
                var about = new Label { Dock = DockStyle.Fill, Text = Brand.Name + " · " + Brand.Version + "\n\nVersão Windows para testes de TI. Código próprio em C# / .NET.\nProjeto Linux relacionado, originalmente inspirado no RESET EXPLODE de GustavoReset (MIT).\n\nNão instala serviços nem drivers. Nenhum teste é iniciado automaticamente.\n\nTestes de CPU e RAM são amostras e podem elevar uso e temperatura.\nO teste de disco usa somente um arquivo temporário próprio e o remove ao terminar.\nTemperaturas disponíveis pela aba Sensores; cobertura depende do hardware/driver. Diagnóstico SMART completo e benchmark de GPU não estão implementados.\n\nRelatórios são salvos somente quando você escolhe a pasta.\nRede de medição: servidores públicos de terceiros, sem envio do inventário.\n\nCódigo-fonte e licenças estão junto da distribuição e no repositório do projeto.", Padding = new Padding(0, 16, 0, 0) }; content.Controls.Add(about); Heading("Sobre o aplicativo", "Ferramentas portáteis para diagnóstico e testes de PCs Windows 10/11 de 64 bits."); break;
        }
    }
    void BuildTests()
    {
        resultsGrid = Grid(); RefreshResults();
        var cpu = Actions();
        var workers = new NumericUpDown { Minimum = 1, Maximum = Environment.ProcessorCount, Value = Math.Max(1, Environment.ProcessorCount / 2), Width = 65, BackColor = Surface, ForeColor = Ink };
        Button(cpu, "CPU · 15 segundos", () => ConfirmRun("CPU", "Carga SHA-256 por 15 segundos. Pode aumentar o uso e a temperatura do processador.", ct => Benchmarks.Cpu((int)workers.Value, TimeSpan.FromSeconds(15), ct)), true);
        cpu.Controls.Add(new Label { Text = "Threads:", AutoSize = true, Padding = new Padding(4, 8, 0, 0) }); cpu.Controls.Add(workers);
        var ram = Actions(); Button(ram, "RAM · verificar 128 MiB", () => ConfirmRun("Memória", "Reserva e verifica 128 MiB com padrões alternados por 15 segundos. Não testa toda a memória física.", ct => { if (Inventory.Memory().Available < 512L * 1048576) throw new IOException("Menos de 512 MiB disponíveis. Feche programas antes deste teste."); return Benchmarks.Memory(128, TimeSpan.FromSeconds(15), ct); }), true);
        var disk = Actions(); Button(disk, "Disco · escolher pasta e testar", () => {
            using var dialog = new FolderBrowserDialog { Description = "Escolha a pasta do volume a testar. Será criado apenas um arquivo temporário de 64 MiB.", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) ConfirmRun("Disco", "Gravar, ler e verificar um arquivo temporário de 64 MiB nesta pasta?\n" + dialog.SelectedPath, ct => Benchmarks.Disk(dialog.SelectedPath, 64, ct));
        }, true);
        Heading("Testes de desempenho", "Escolha um teste e confirme para começar. Use “Parar teste” para cancelar. Compare resultados apenas com o mesmo método e configuração.");
    }
    void BuildNetwork()
    {
        resultsGrid = Grid(); RefreshResults();
        var speed = Actions(); Button(speed, "Medir velocidade da internet", () => ConfirmRun("Internet", "Download e upload com até 8 conexões, cerca de 8 segundos por fase. Pode consumir até 2 GiB + seleção de servidor. Continuar?", ct => NetworkDiagnostics.WithTimeout(token => SpeedTest.Run(new Progress<SpeedProgress>(p => status.Text = p.Stage + (p.Mbps > 0 ? " · " + p.Mbps.ToString("N2") + " Mbps" : "")), token), TimeSpan.FromSeconds(90), ct)), true);
        var ping = Actions(); var host = new TextBox { Text = "1.1.1.1", Width = 220, BackColor = Surface, ForeColor = Ink, Margin = new Padding(0, 4, 12, 0) }; ping.Controls.Add(host);
        Button(ping, "Testar DNS e ping", () => _ = Run("DNS e ping", ct => NetworkDiagnostics.WithTimeout(token => NetworkDiagnostics.PingHost(host.Text, token), TimeSpan.FromSeconds(45), ct), cancellableTest: true), true);
        var connectivity = Actions();
        Button(connectivity, "Testar acesso HTTPS", () => _ = Run("Conectividade HTTPS", ct => NetworkDiagnostics.WithTimeout(NetworkDiagnostics.Internet, TimeSpan.FromSeconds(15), ct), cancellableTest: true), true);
        Button(connectivity, "Testar TCP443 do destino", () => _ = Run("Conexão TCP", ct => NetworkDiagnostics.Tcp(host.Text, 443, ct), cancellableTest: true), true);
        Heading("Rede e internet", "Ping/DNS para um destino e medição nativa de velocidade. Firewall ou servidor podem limitar respostas e taxas. Inventário não é transmitido.");
    }
    void BuildPeripherals()
    {
        var explanation = new Label { Dock = DockStyle.Fill, Text = "TELA\nO teste alterna cores sólidas na tela escolhida. Observe pixels, manchas e cintilação. Pressione Esc para sair.\n\nTECLADO E MOUSE\nAbre uma janela que mostra as teclas e botões recebidos. Não captura digitação fora da janela.\n\nÁUDIO\nReproduz uma sequência de tons: esquerdo, direito e ambos. Confira o volume e o dispositivo de saída do Windows.\n\nInspeções visuais e de áudio precisam da sua avaliação; não geram aprovação automática.", Padding = new Padding(0, 16, 0, 0) }; content.Controls.Add(explanation);
        var actions = Actions(); Button(actions, "Tela · cores", () => new DisplayTest().Show(this)); Button(actions, "Teclado e mouse", () => new InputTest().Show(this)); Button(actions, "Áudio · estéreo", () => _ = RunAudio(), true);
        Heading("Tela e periféricos", "Testes manuais para conferir os dispositivos conectados a este PC.");
    }
    async Task RunAudio() { await Run("Áudio estéreo", async ct => { await AudioTest.Play(ct); return TestResult.Done("Áudio estéreo", ("Sequência", "Esquerdo → direito → ambos"), ("Avaliação", "Confirme pelo ouvido; reprodução não comprova som físico.")); }, cancellableTest: true); }
    void ConfirmRun(string name, string message, Func<CancellationToken, Task<TestResult>> job) {
        if (task is not null) return;
        if (MessageBox.Show(this, message, Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK) _ = Run(name, job, cancellableTest: true);
    }
    async Task Run(string name, Func<CancellationToken, Task<TestResult>> job, bool cancellableTest = false)
    {
        if (task is not null) return;
        task = new CancellationTokenSource(); stop.Enabled = cancellableTest; stop.Visible = cancellableTest; foreach (var b in runButtons) b.Enabled = false; status.Text = name + " · executando…";
        try { var result = await job(task.Token); results.Add(result); status.Text = result.Teste + " · " + result.Estado; }
        catch (OperationCanceledException) { results.Add(new(name, "Cancelado", new() { ["Observação"] = "Teste interrompido; não representa aprovação ou falha do hardware." }, DateTimeOffset.Now)); status.Text = name + " · cancelado"; }
        catch (Exception e) { results.Add(new(name, "Não concluído", new() { ["Erro"] = e.Message }, DateTimeOffset.Now)); status.Text = name + " · não concluído"; MessageBox.Show(this, e.Message, Brand.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { task.Dispose(); task = null; stop.Enabled = false; stop.Visible = false; foreach (var b in runButtons) b.Enabled = true; RefreshResults(); if (closing) Close(); }
    }
    async Task LoadInventory()
    {
        if (task is not null) return;
        status.Text = "Consultando hardware e sistema…";
        await Run("Inventário", async ct => { inventory = await Task.Run(Inventory.Collect, ct); device = await Task.Run(DeviceDetection.Read, ct); return TestResult.Done("Inventário", ("Campos consultados", inventory.Count)); });
        if (!closing) ShowPage(menu.SelectedIndex);
    }
    void RefreshProcesses() { if (processGrid is not null) { try { processGrid.DataSource = Inventory.Processes(); } catch (Exception e) { status.Text = "Processos: " + e.Message; } } }
    void UpdateMemory() { if (metrics is not null) { var m = Inventory.Memory(); metrics.Text = "RAM total: " + Inventory.GiB(m.Total) + "     Disponível: " + Inventory.GiB(m.Available) + "\nEm uso: " + Inventory.GiB(m.Total - m.Available); } }
    void RefreshResults() { if (resultsGrid is not null) resultsGrid.DataSource = results.AsEnumerable().Reverse().Select(r => new { Teste = r.Teste, Estado = r.Estado, Horário = r.Data.LocalDateTime.ToString("g"), Resultado = OperationResults.Summary(r) }).ToList(); }
    void Export()
    {
        using var dialog = new FolderBrowserDialog { Description = "Salvar relatório HTML e JSON nesta pasta", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var data = new { Aplicativo = Brand.Name, Versao = Brand.Version, Computador = Environment.MachineName, GeradoEm = DateTimeOffset.Now, Inventario = inventory, Testes = results.ToArray() };
        string prefix = "diagnostico-" + string.Concat(Environment.MachineName.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_')) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try { File.WriteAllText(Path.Combine(dialog.SelectedPath, prefix + ".html"), Reports.Html(data), new UTF8Encoding(false)); File.WriteAllText(Path.Combine(dialog.SelectedPath, prefix + ".json"), Reports.Json(data), new UTF8Encoding(false)); status.Text = "HTML + JSON salvos em " + dialog.SelectedPath; }
        catch (Exception e) { MessageBox.Show(this, "Não foi possível salvar: " + e.Message, Brand.Name); }
    }
    public void VerifyUi(List<InfoRow> snapshot) { inventory = snapshot; if (menu.Items.Count != 16) throw new Exception("Páginas ausentes"); if (task is not null) throw new Exception("Carga automática indevida"); for (int i = 0; i < pages.Length; i++) menu.SelectedIndex = i; menu.SelectedIndex = 0; }
}
