using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Diagnostico.Windows;

public record InfoRow(string Grupo, string Campo, string Valor);
public record ProcessRow(string Programa, int PID, double MemoriaMiB, double PercentualRAM);

public static class Inventory
{
    public static List<InfoRow> Collect()
    {
        var rows = new List<InfoRow>();
        void Add(string group, string label, object? value) => rows.Add(new(group, label, value?.ToString() ?? "Indisponível"));
        Add("Sistema", "Computador", Environment.MachineName);
        Add("Sistema", "Arquitetura", RuntimeInformation.OSArchitecture);
        Add("Sistema", "Aplicativo", Brand.Name + " " + Brand.Version + " · Windows portátil");
        Add("Sistema", "Tempo desde inicialização", TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\d\ hh\h\ mm\m"));
        Query(rows, "Sistema", "Win32_OperatingSystem", new() { ["Caption"] = "Windows", ["Version"] = "Versão", ["BuildNumber"] = "Build" });
        Query(rows, "Equipamento", "Win32_ComputerSystem", new() { ["Manufacturer"] = "Fabricante", ["Model"] = "Modelo" });
        Query(rows, "Placa-mãe", "Win32_BaseBoard", new() { ["Manufacturer"] = "Fabricante", ["Product"] = "Modelo" });
        Query(rows, "BIOS", "Win32_BIOS", new() { ["SMBIOSBIOSVersion"] = "Versão" });
        Query(rows, "CPU", "Win32_Processor", new() { ["Name"] = "Modelo", ["NumberOfCores"] = "Núcleos", ["NumberOfLogicalProcessors"] = "Threads", ["MaxClockSpeed"] = "Clock anunciado (MHz)" });
        Query(rows, "RAM · módulos", "Win32_PhysicalMemory", new() { ["DeviceLocator"] = "Slot", ["Manufacturer"] = "Fabricante", ["Capacity"] = "Capacidade (GiB)", ["ConfiguredClockSpeed"] = "Velocidade configurada (MT/s)" });
        Query(rows, "Vídeo", "Win32_VideoController", new() { ["Name"] = "Modelo", ["DriverVersion"] = "Driver", ["CurrentHorizontalResolution"] = "Largura atual", ["CurrentVerticalResolution"] = "Altura atual", ["CurrentRefreshRate"] = "Atualização (Hz)" });
        Query(rows, "Discos físicos", "Win32_DiskDrive", new() { ["Model"] = "Modelo", ["InterfaceType"] = "Interface informada", ["Size"] = "Capacidade (GiB)", ["Status"] = "Estado WMI" });
        Query(rows, "Bateria", "Win32_Battery", new() { ["Name"] = "Bateria", ["EstimatedChargeRemaining"] = "Carga (%)", ["BatteryStatus"] = "Estado WMI (código)" });
        foreach (var d in DriveInfo.GetDrives()) {
            try { if (d.IsReady) { Add("Volumes", d.Name + " · tipo", d.DriveType); Add("Volumes", d.Name + " · formato", d.DriveFormat); Add("Volumes", d.Name + " · capacidade", GiB(d.TotalSize)); Add("Volumes", d.Name + " · livre", GiB(d.AvailableFreeSpace)); } }
            catch (IOException) { Add("Volumes", d.Name, "Não foi possível consultar."); }
        }
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)) {
            Add("Rede", n.Name, n.Description); Add("Rede", n.Name + " · estado", n.OperationalStatus);
            Add("Rede", n.Name + " · link anunciado", n.Speed > 0 ? (n.Speed / 1000000.0).ToString("N0") + " Mbps" : "Indisponível");
            try {
                var ip = n.GetIPProperties();
                Add("Rede", n.Name + " · IP local", string.Join(", ", ip.UnicastAddresses.Select(a => a.Address.ToString())));
                Add("Rede", n.Name + " · gateway", string.Join(", ", ip.GatewayAddresses.Select(a => a.Address.ToString())));
                Add("Rede", n.Name + " · DNS", string.Join(", ", ip.DnsAddresses));
            } catch (NetworkInformationException) { Add("Rede", n.Name + " · IP", "Indisponível"); }
        }
        Query(rows, "Dispositivos com erro", "Win32_PnPEntity", new() { ["Name"] = "Dispositivo", ["ConfigManagerErrorCode"] = "Código do erro" }, "ConfigManagerErrorCode <> 0");
        Add("Limites", "Temperaturas / SMART detalhado", "Não consultados por este aplicativo. Estado WMI não substitui SMART nem inspeção física.");
        return rows;
    }

    internal static void Query(List<InfoRow> rows, string group, string className, Dictionary<string, string> fields, string? where = null)
    {
        try {
            using var query = new ManagementObjectSearcher("root\\cimv2", "SELECT " + string.Join(",", fields.Keys) + " FROM " + className + (where is null ? "" : " WHERE " + where));
            query.Options.Timeout = TimeSpan.FromSeconds(8);
            using var objects = query.Get(); int index = 0;
            foreach (ManagementObject obj in objects) {
                using (obj) {
                    index++;
                    foreach (var field in fields) {
                        object? value = obj[field.Key]; string output = value?.ToString() ?? "Indisponível";
                        if ((field.Key is "Capacity" or "Size") && ulong.TryParse(output, out ulong size)) output = (size / 1073741824.0).ToString("N2");
                        rows.Add(new(group, (index > 1 ? "#" + index + " · " : "") + field.Value, output));
                    }
                }
            }
            if (index == 0) rows.Add(new(group, "Consulta", "Nenhum registro informado pelo Windows."));
        } catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or COMException) {
            rows.Add(new(group, "Consulta", "Indisponível: " + e.Message));
        }
    }

    public static List<ProcessRow> Processes()
    {
        double total = Memory().Total;
        var rows = new List<ProcessRow>();
        foreach (var p in Process.GetProcesses()) {
            using (p) {
                try { long bytes = p.WorkingSet64; rows.Add(new(p.ProcessName, p.Id, Math.Round(bytes / 1048576.0, 1), total > 0 ? Math.Round(bytes * 100.0 / total, 2) : 0)); }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
        return rows.OrderByDescending(x => x.MemoriaMiB).ToList();
    }
    public static string GiB(long value) => (value / 1073741824.0).ToString("N2") + " GiB";
    public static (long Total, long Available) Memory() {
        var m = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref m) ? ((long)m.TotalPhys, (long)m.AvailPhys) : (0, 0);
    }
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
