namespace Diagnostico;

public record DeviceProfile(string Kind, string Manufacturer, string Model, string Evidence)
{
    public bool Portable => Kind == "Notebook / portátil";
    public bool Asus => Manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase) || Manufacturer.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase);
    public static DeviceProfile Detect(string manufacturer, string model, int systemType, int[] chassis)
    {
        bool portable = chassis.Any(c => c is 8 or 9 or 10 or 14 or 30 or 31 or 32) || systemType == 2;
        bool desktop = !portable && (systemType == 1 || chassis.Any(c => c is 3 or 4 or 5 or 6 or 7 or 13 or 15 or 16 or 35 or 36));
        return new(portable ? "Notebook / portátil" : desktop ? "Desktop" : "Tipo não confirmado", manufacturer, model, $"PCSystemType={systemType}; chassis={string.Join(",", chassis)}. Bateria isolada não identifica notebook (pode ser nobreak).");
    }
    public string[] Suggested(bool windows10) => new[] { "suggestions", "visuals", "app:Microsoft.OutlookForWindows", "app:Microsoft.549981C3F5F10" }
        .Concat(windows10 ? new[] { "taskbar", "feeds" } : []).Concat(Kind == "Desktop" ? new[] { "power-ac" } : []).ToArray();
}
