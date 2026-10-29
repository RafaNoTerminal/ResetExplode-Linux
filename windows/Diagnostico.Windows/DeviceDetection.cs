using System.Management;
namespace Diagnostico.Windows;

internal static class DeviceDetection
{
    internal static DeviceProfile Read()
    {
        string manufacturer = "Não informado", model = "Não informado"; int type = 0; var chassis = new List<int>();
        try {
            using var search = new ManagementObjectSearcher("SELECT Manufacturer,Model,PCSystemType FROM Win32_ComputerSystem");
            using var values = search.Get();
            foreach (ManagementObject item in values) using (item) { manufacturer = Convert.ToString(item["Manufacturer"]) ?? manufacturer; model = Convert.ToString(item["Model"]) ?? model; type = Convert.ToInt32(item["PCSystemType"]); }
            using var enclosure = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"); using var cases = enclosure.Get();
            foreach (ManagementObject item in cases) using (item) if (item["ChassisTypes"] is ushort[] list) chassis.AddRange(list.Select(x => (int)x));
        } catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return DeviceProfile.Detect(manufacturer, model, type, chassis.ToArray()) with { Evidence = "Detecção parcial: " + e.Message }; }
        return DeviceProfile.Detect(manufacturer, model, type, chassis.ToArray());
    }
}
