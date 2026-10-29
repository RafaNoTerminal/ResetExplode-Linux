using LibreHardwareMonitor.Hardware;
namespace Diagnostico.Windows;

public record SensorRow(string Grupo, string Tipo, string Sensor, string Atual, string Minimo, string Maximo, string Unidade, string Origem);
internal sealed class SensorMonitor : IDisposable
{
    readonly object gate = new(); Computer? computer;
    string baseNotice = "";
    internal bool Active { get; private set; }
    internal string Notice { get; private set; } = "Leitura não iniciada.";
    internal List<SensorRow> Read(bool start = false)
    {
        lock (gate) {
            if (start && computer is null) {
                var type = typeof(Computer).Assembly.GetType("LibreHardwareMonitor.PawnIo.PawnIo");
                bool pawn = (bool?)type?.GetProperty("IsInstalled", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(null) == true;
                computer = new Computer { IsCpuEnabled = pawn, IsGpuEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true, IsMotherboardEnabled = pawn, IsBatteryEnabled = true, IsNetworkEnabled = false, IsControllerEnabled = false };
                try { computer.Open(); Active = true; Notice = pawn ? "LibreHardwareMonitor · suporte PawnIO detectado. Somente leitura; não controla ventoinhas." : "PawnIO não detectado: CPU/placa-mãe podem ficar sem temperatura. GPU, armazenamento e outros sensores compatíveis continuam disponíveis. O RBT não instala driver silenciosamente."; }
                catch { computer.Close(); computer = null; throw; }
            }
            if (start) baseNotice = Notice; else Notice = baseNotice;
            var rows = new List<SensorRow>(); if (computer is null) return rows;
            void Visit(IHardware hardware) {
                try { hardware.Update(); }
                catch (Exception e) { rows.Add(new(hardware.Name, "Consulta", "Erro", e.Message, "", "", "", "LibreHardwareMonitor")); }
                foreach (var sensor in hardware.Sensors) {
                    string Format(float? value) => value.HasValue && float.IsFinite(value.Value) ? value.Value.ToString("N2") : "Indisponível";
                    string unit = sensor.SensorType switch { SensorType.Temperature => "°C", SensorType.Fan => "RPM", SensorType.Voltage => "V", SensorType.Power => "W", SensorType.Load or SensorType.Level or SensorType.Control => "%", SensorType.Clock => "MHz", SensorType.Data => "GB", SensorType.SmallData => "MB", SensorType.Throughput => "B/s", SensorType.Current => "A", SensorType.Energy => "mWh", _ => "" };
                    rows.Add(new(hardware.Name, sensor.SensorType.ToString(), sensor.Name, Format(sensor.Value), Format(sensor.Min), Format(sensor.Max), unit, "LibreHardwareMonitor · " + sensor.Identifier));
                }
                foreach (var child in hardware.SubHardware) Visit(child);
            }
            foreach (var hardware in computer.Hardware) Visit(hardware);
            if (!rows.Any(r => r.Tipo == "Temperature" && r.Atual != "Indisponível")) Notice += " Nenhuma temperatura válida foi exposta nesta amostra.";
            return rows;
        }
    }
    public void Dispose() { lock (gate) { computer?.Close(); computer = null; Active = false; Notice = "Leitura interrompida. Última amostra preservada."; } }
}

public sealed partial class MainForm
{
    readonly SensorMonitor sensorMonitor = new();
    readonly System.Windows.Forms.Timer sensorTimer = new() { Interval = 2000 };
    List<SensorRow> sensorRows = []; DateTimeOffset? sensorAt; bool sensorBusy, sensorWired;
    DataGridView? sensorGrid; Label? sensorNotice;
    async Task RefreshSensors(bool start = false)
    {
        if (sensorBusy) return; sensorBusy = true;
        try {
            sensorRows = await Task.Run(() => sensorMonitor.Read(start)); sensorAt = DateTimeOffset.Now;
            if (sensorGrid is { IsDisposed: false }) sensorGrid.DataSource = sensorRows.ToList();
            if (sensorNotice is { IsDisposed: false }) sensorNotice.Text = sensorMonitor.Notice + "\nAtualizado: " + sensorAt.Value.ToLocalTime().ToString("T") + " · mínimos/máximos desde o início da leitura.";
        } catch (Exception e) { if (sensorNotice is { IsDisposed: false }) sensorNotice.Text = "Leitura não disponível: " + e.Message; sensorTimer.Stop(); if (start) throw; }
        finally { sensorBusy = false; }
    }
    void BuildSensors()
    {
        sensorGrid = Grid(); sensorGrid.DataSource = sensorRows.ToList(); sensorNotice = new Label { Dock = DockStyle.Top, Height = 95, ForeColor = Color.Silver, Text = sensorMonitor.Notice }; content.Controls.Add(sensorNotice);
        if (!sensorWired) { sensorTimer.Tick += async (_, _) => { if (sensorMonitor.Active) await RefreshSensors(); }; sensorWired = true; }
        var controls = Actions();
        Button(controls, "Iniciar leitura", () => _ = Run("Sensores", async ct => { await RefreshSensors(start: true); if (sensorMonitor.Active) sensorTimer.Start(); return TestResult.Done("Sensores", ("Iniciado", sensorMonitor.Active), ("Observação", sensorMonitor.Notice)); }), true);
        Button(controls, "Parar leitura", () => { sensorTimer.Stop(); sensorMonitor.Dispose(); if (!sensorNotice.IsDisposed) sensorNotice.Text = sensorMonitor.Notice; });
        Button(controls, "Salvar amostra no relatório", () => { results.Add(TestResult.Done("Sensores · amostra", ("Horário", sensorAt), ("Sensores", sensorRows.ToArray()), ("Observação", sensorMonitor.Notice))); status.Text = "Amostra incluída em Resultados; exporte HTML + JSON para guardar."; });
        var support = Actions();
        Button(support, "Instalar suporte de sensores", () => {
            if (task is not null) return;
            if (MessageBox.Show(this, "Instalar o driver opcional PawnIO2.2.0 para leitura de sensores CPU/placa-mãe?\n\nDownload do projeto original, SHA256 fixo e assinatura digital conferidos. Requer administrador e pode pedir reinício. Feche e reabra o RBT depois de instalar. Não é necessário para todos os sensores GPU/disco.\n\nO RBT não enfraquece políticas de drivers/segurança para instalar.", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            sensorTimer.Stop(); sensorMonitor.Dispose(); _ = Run("Instalar suporte de sensores", SensorSupport.Install);
        }, true);
        Button(support, "Suporte PawnIO", () => OpenLink("https://github.com/namazso/PawnIO"));
        Heading("Sensores de hardware", "Temperaturas, RPM, carga, clocks, consumo, tensões e sensores de discos/bateria disponíveis. Leitura a cada2s; mínimos/máximos da sessão. Não altera rotação ou parâmetros do hardware. ACPI não é usado como temperatura da CPU.");
    }
}
