using System.Management;
using System.Text.Json;
namespace Diagnostico.Windows;

internal record HubPowerEntry(string Instance, bool Enable);
internal record HubPowerBackup(string Computer, DateTimeOffset Date, HubPowerEntry[] Entries);
internal static class UsbHubPower
{
    internal static bool RootHub(string instance) => instance.StartsWith(@"USB\ROOT_HUB\", StringComparison.OrdinalIgnoreCase) || instance.StartsWith(@"USB\ROOT_HUB20\", StringComparison.OrdinalIgnoreCase) || instance.StartsWith(@"USB\ROOT_HUB30\", StringComparison.OrdinalIgnoreCase);
    internal static List<HubPowerEntry> Read()
    {
        var rows = new List<HubPowerEntry>();
        using var search = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName,Enable FROM MSPower_DeviceEnable"); using var results = search.Get();
        foreach (ManagementObject item in results) using (item) {
            string instance = Convert.ToString(item["InstanceName"]) ?? "";
            if (RootHub(instance)) rows.Add(new(instance, Convert.ToBoolean(item["Enable"])));
        }
        return rows;
    }
    internal static void Write(HubPowerEntry entry)
    {
        if (!RootHub(entry.Instance)) throw new IOException("Dispositivo fora da lista de hubs raiz permitidos.");
        using var search = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSPower_DeviceEnable"); using var results = search.Get(); bool found = false;
        foreach (ManagementObject item in results) using (item) {
            if (!string.Equals(Convert.ToString(item["InstanceName"]), entry.Instance, StringComparison.OrdinalIgnoreCase)) continue;
            found = true; item["Enable"] = entry.Enable; item.Put(); item.Get();
            if (Convert.ToBoolean(item["Enable"]) != entry.Enable) throw new IOException("O driver não confirmou o valor solicitado.");
        }
        if (!found) throw new IOException("Hub não está disponível ou o driver não expõe a opção.");
    }
    internal static TestResult Apply()
    {
        if (!Automation.Admin) throw new UnauthorizedAccessException("Administrador necessário.");
        var before = Read(); if (before.Count == 0) throw new IOException("Os drivers não expõem gerenciamento de energia dos hubs raiz via WMI. Nenhuma alteração foi feita; confira manualmente no Gerenciador de Dispositivos.");
        Directory.CreateDirectory(Automation.BackupDirectory);
        string file = Path.Combine(Automation.BackupDirectory, "hubs-usb-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(new HubPowerBackup(Environment.MachineName, DateTimeOffset.Now, before.ToArray())));
        return Change(before.Select(b => b with { Enable = false }).ToArray(), file, "Aplicar teste de energia dos hubs USB");
    }
    internal static TestResult Restore(string file)
    {
        if (!Automation.Admin) throw new UnauthorizedAccessException("Administrador necessário.");
        var backup = JsonSerializer.Deserialize<HubPowerBackup>(File.ReadAllText(file)) ?? throw new IOException("Backup inválido.");
        if (backup.Computer != Environment.MachineName || backup.Entries.Any(e => !RootHub(e.Instance))) throw new IOException("Backup não pertence a este PC ou contém dispositivos fora do escopo.");
        return Change(backup.Entries, file, "Restaurar energia dos hubs USB");
    }
    static TestResult Change(HubPowerEntry[] entries, string file, string title)
    {
        var rows = new List<OperationRow>();
        foreach (var entry in entries) {
            try { Write(entry); rows.Add(new(title, entry.Instance, "Dispositivo · tomada e bateria", "Aplicado", "Gerenciamento de energia pelo Windows Enable=" + entry.Enable + "; valor relido e confirmado.")); }
            catch (Exception e) { rows.Add(new(title, entry.Instance, "Dispositivo", "Erro", e.Message)); }
        }
        return new(title, rows.Any(r => r.Failed) ? "Concluído com falhas" : "Concluído", new() { ["Backup"] = file, ["Etapas"] = rows.ToArray(), ["Observação"] = "Teste temporário do checklist Vivobook. Esta opção do dispositivo vale também na bateria e pode aumentar consumo. Restaure se não resolver. Nenhum dispositivo foi desativado/reiniciado." }, DateTimeOffset.Now);
    }
}
