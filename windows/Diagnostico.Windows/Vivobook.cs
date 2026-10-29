using System.Diagnostics.Eventing.Reader;

namespace Diagnostico.Windows;

public static class Vivobook
{
    public static List<InfoRow> Collect() {
        var rows = new List<InfoRow>();
        Inventory.Query(rows, "Modelo / BIOS", "Win32_ComputerSystem", new() { ["Manufacturer"] = "Fabricante", ["Model"] = "Modelo" });
        Inventory.Query(rows, "Modelo / BIOS", "Win32_BIOS", new() { ["SMBIOSBIOSVersion"] = "Versão BIOS" });
        Inventory.Query(rows, "Windows", "Win32_OperatingSystem", new() { ["Caption"] = "Edição", ["Version"] = "Versão", ["BuildNumber"] = "Build" });
        Inventory.Query(rows, "Driver de vídeo / rede / USB", "Win32_PnPSignedDriver", new() { ["DeviceName"] = "Dispositivo", ["DriverProviderName"] = "Fornecedor", ["DriverVersion"] = "Versão", ["DriverDate"] = "Data WMI", ["InfName"] = "Arquivo INF", ["IsSigned"] = "Assinado" }, "DeviceClass='DISPLAY' OR DeviceClass='NET' OR DeviceClass='USB'");
        var profile = DeviceDetection.Read();
        rows.Add(new("Detecção", "Tipo de equipamento", profile.Kind + " · " + profile.Evidence));
        rows.Add(new("Caso do histórico", "Sintoma", "Monitor externo piscando ou sumindo; comparar HDMI direta, cabo e comportamento após tampa/bloqueio."));
        rows.Add(new("Caso do histórico", "Driver AMD", "Confira fornecedor Advanced Micro Devices no vídeo. Microsoft Basic Display Adapter/driver genérico é pendência; o modelo exato define o pacote ASUS. 25H2 é versão do Windows, não de driver."));
        try { foreach (var hub in UsbHubPower.Read()) rows.Add(new("Hubs USB · energia", hub.Instance, "Windows pode gerenciar energia: " + hub.Enable)); }
        catch (Exception e) { rows.Add(new("Hubs USB · energia", "Consulta", "Não exposto pelo driver ou sem acesso: " + e.Message)); }
        foreach (var screen in Screen.AllScreens) rows.Add(new("Monitores detectados", screen.DeviceName, $"{screen.Bounds.Width} × {screen.Bounds.Height} · posição {screen.Bounds.X},{screen.Bounds.Y} · principal={screen.Primary}"));
        try {
            var plan = Power.Active(); rows.Add(new("Energia", "Plano ativo", plan.ToString()));
            foreach (var action in AutomationCatalog.All.Where(a => a.Id is "usb-ac" or "pcie-ac")) {
                var setting = action.Power![0];
                try { rows.Add(new("Energia · tomada", action.Title, "Índice atual: " + Power.Read(plan, setting.Subgroup, setting.Setting) + " (0 = desligado)")); }
                catch (Exception e) { rows.Add(new("Energia · tomada", action.Title, "Indisponível: " + e.Message)); }
            }
        } catch (Exception e) { rows.Add(new("Energia", "Consulta", e.Message)); }
        try {
            const string filter = "*[System[(EventID=41 or EventID=4101 or EventID=6008 or EventID=219 or EventID=17 or EventID=18 or EventID=153 or EventID=129) and TimeCreated[timediff(@SystemTime) <= 259200000]]]";
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, filter) { ReverseDirection = true });
            int count = 0;
            while (count < 50) {
                using var record = reader.ReadEvent(); if (record is null) break;
                string text; try { text = record.FormatDescription() ?? "Sem descrição."; } catch (EventLogException) { text = "Descrição indisponível."; }
                rows.Add(new("Eventos · últimas 72 h", record.TimeCreated + " · " + record.ProviderName + " · ID " + record.Id, text[..Math.Min(text.Length, 2000)])); count++;
            }
            if (count == 0) rows.Add(new("Eventos", "Resultado", "Nenhum evento correspondente nesta amostra; isso não descarta falha."));
        } catch (Exception e) when (e is EventLogException or UnauthorizedAccessException) { rows.Add(new("Eventos", "Consulta", "Indisponível: " + e.Message)); }
        rows.Add(new("Checklist", "1 · isolar conexão", "Teste outro cabo/entrada e a HDMI direta, sem adaptador. Compare se o problema também aparece na tela interna."));
        rows.Add(new("Checklist", "2 · driver / modelo", "Confira modelo exato e driver de vídeo compatível no suporte ASUS. A versão 25H2 é versão do Windows; não identifica sozinha um driver defeituoso. Este app não troca BIOS/driver automaticamente."));
        rows.Add(new("Checklist", "3 · confirmar alteração", "Faça uma alteração por vez. Teste monitor, tampa, bloqueio/desbloqueio e uso prolongado. USB/PCIe são testes temporários, somente na tomada; use o backup para restaurar."));
        rows.Add(new("Checklist", "4 · MyASUS", "Se disponível, execute Diagnóstico do Sistema do MyASUS e confira recursos de imagem/energia compatíveis com o modelo. Recursos variam por equipamento."));
        rows.Add(new("Fontes", "ASUS · monitor externo", "https://www.asus.com/us/support/faq/1047158/"));
        rows.Add(new("Fontes", "ASUS · diagnóstico MyASUS", "https://www.asus.com/global/support/faq/1045716/"));
        rows.Add(new("Fontes", "Microsoft · suspensão seletiva", "https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-selective-suspend"));
        return rows;
    }
}
