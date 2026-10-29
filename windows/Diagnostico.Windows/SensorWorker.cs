using LibreHardwareMonitor.Hardware;
namespace Diagnostico.Windows;

internal sealed class SensorHardwareReader : IDisposable
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
                computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true, IsMotherboardEnabled = pawn, IsBatteryEnabled = true, IsNetworkEnabled = false, IsControllerEnabled = false };
                try { computer.Open(); Active = true; Notice = pawn ? "LibreHardwareMonitor · suporte PawnIO detectado. Somente leitura; não controla ventoinhas." : "PawnIO não detectado: CPU consultada, mas sensores internos podem exigir instalar o suporte. GPU, armazenamento e outros sensores compatíveis continuam disponíveis. O RBT não instala driver silenciosamente."; }
                catch { computer.Close(); computer = null; throw; }
            }
            if (baseNotice.Length == 0) baseNotice = Notice; else Notice = baseNotice;
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


internal record SensorSnapshot(List<SensorRow> Rows, string Notice);
internal static class SensorWorker
{
    internal static int Run(bool fixture = false)
    {
        using var reader = new SensorHardwareReader(); bool started = false;
        try {
            Console.InputEncoding = System.Text.Encoding.UTF8; Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            string? command;
            while ((command = Console.ReadLine()) is not null && command != "close") {
                if (command != "read") continue;
                try {
                    var rows = fixture ? new List<SensorRow> { new("CPU fixture", "Temperature", "Package", "45", "40", "50", "°C", "Fixture; nenhuma leitura de hardware") } : reader.Read(start: !started);
                    started = true;
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new SensorSnapshot(rows, fixture ? "Fixture IPC, não é temperatura real." : reader.Notice)));
                } catch (Exception e) { Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new SensorSnapshot([], "Erro na leitura: " + e.Message))); }
            }
            return 0;
        } catch { return 1; }
    }
}
