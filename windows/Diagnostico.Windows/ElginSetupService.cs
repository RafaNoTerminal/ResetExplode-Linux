using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
namespace Diagnostico.Windows;

internal static class ElginSetupService
{
    internal const string DriverName = "ELGIN i9(USB)";
    internal const string DriverHash = "3af821021d88cccca74bf6a50e14b298eeb0005757336ed227b248848817f676";
    internal const string UtilityHash = "9c2818bfc6cc17944437921d0c311069b8a85013bd63cfb4ea7f8b491f7ddb59";
    internal static string Materials => Path.Combine(AppContext.BaseDirectory, "DRIVERS", "Elgin i9 Config");
    internal static string ProfileFile => Path.Combine(AppContext.BaseDirectory, "Empresa", "elgin-comprovantes.rbt-profile.json");
    internal static async Task<TestResult> InstallOriginal(bool utility) {
        string title = utility ? "Utilitário Elgin i9" : "Driver Elgin i9 1.8.0";
        string source = Path.Combine(Materials, utility ? "ELGIN_i9_Utility_Setup V1.2.2.31.exe" : "ELGIN Printer Driver_v-1.8.0 1.exe");
        string expected = utility ? UtilityHash : DriverHash;
        var directory = ActivationService.PrivateDirectory();
        try {
            string local = Path.Combine(directory.FullName, "elgin-setup.exe");
            await using (var input = File.OpenRead(source)) await using (var output = new FileStream(local, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await input.CopyToAsync(output);
            using (var input = File.OpenRead(local)) if (!Convert.ToHexString(await SHA256.HashDataAsync(input)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("O instalador difere do arquivo fornecido por Rafael. Nenhum instalador foi executado.");
            // The supplied historical EXEs are unsigned. Their exact hashes are pinned;
            // run the original interactive UI only after explicit disclosure/confirmation.
            using var process = Process.Start(new ProcessStartInfo(local) { UseShellExecute = true }) ?? throw new IOException("Instalador não iniciou.");
            await process.WaitForExitAsync();
            bool present = false;
            if (!utility) { using var inventory = await CompanyService.Script(PrinterScripts.Inventory, CancellationToken.None); present = inventory.RootElement.GetProperty("Drivers").EnumerateArray().Any(d => d.GetProperty("Name").GetString() == DriverName); }
            return new(title, process.ExitCode == 0 && (utility || present) ? utility ? "Instalador concluiu; abrir utilitário para configurar logotipo" : "Driver confirmado no spooler; filas podem ser instaladas" : process.ExitCode == 3010 ? "Reinício solicitado; faça manualmente e confira o driver" : "Instalação não confirmada", new() { ["Retorno"] = process.ExitCode, ["SHA256"] = expected, ["Assinatura EXE"] = "Ausente no pacote fornecido; original executado com confirmação explícita.", ["Driver encontrado"] = utility ? null : present, ["Observação"] = "Instalador original interativo; não assume que código zero comprova impressão. O RBT não importa certificados nem enfraquece políticas de drivers por código próprio." }, DateTimeOffset.Now);
        } finally { try { directory.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    internal static async Task CheckQueue(string name, CompanyPrinter? expected = null) {
        using var inventory = await CompanyService.Script(PrinterScripts.Inventory, CancellationToken.None);
        var matches = inventory.RootElement.GetProperty("Printers").EnumerateArray().Where(p => p.GetProperty("Name").GetString() == name).ToArray();
        if (matches.Length != 1 || matches[0].GetProperty("DriverName").GetString() != DriverName) throw new IOException("Fila ausente ou driver diferente de ELGIN i9(USB). Instale o driver e a fila primeiro.");
        if (expected is not null) {
            string? port = matches[0].GetProperty("PortName").GetString();
            if (!inventory.RootElement.GetProperty("Ports").EnumerateArray().Any(p => p.GetProperty("Name").GetString() == port && p.GetProperty("PrinterHostAddress").GetString() == expected.Ip)) throw new IOException("O destino da fila difere do catálogo. Nenhuma preferência foi aplicada.");
        }
        string data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name));
        var jobs = await MainForm.UpdateProcess("$ErrorActionPreference='Stop';$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + data + "'));if(@(Get-PrintJob -PrinterName $p -ErrorAction Stop).Count){throw 'Fila com trabalhos pendentes. Aguarde antes de configurar/restaurar.'}", CancellationToken.None);
        if (jobs.ExitCode != 0) throw new IOException(jobs.Error);
    }
    internal static ReceiptProfile LoadProfile(string? file = null) {
        var profile = JsonSerializer.Deserialize<ReceiptProfile>(File.ReadAllText(file ?? ProfileFile)) ?? throw new IOException("Perfil completo inválido."); profile.Validate(); return profile;
    }
    internal static void SaveProfile(ReceiptProfile profile) {
        profile.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(ProfileFile)!);
        if (File.Exists(ProfileFile)) File.Copy(ProfileFile, ProfileFile + ".antes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        string temp = ProfileFile + ".novo"; File.WriteAllText(temp, Reports.Json(profile)); File.Move(temp, ProfileFile, overwrite: true);
    }
    internal static async Task<TestResult> Apply(ReceiptProfile profile, CompanyPrinter[] printers) {
        if (printers.Length == 0 || printers.Any(p => !p.Receipt)) throw new ArgumentException("Marque filas de comprovantes.");
        profile.Validate(); var rows = new List<OperationRow>();
        foreach (var printer in printers) {
            try {
                await CheckQueue(printer.Name, printer);
                string backup = await Task.Run(() => { using var preferences = new ElginPreferences(printer.Name); return preferences.Apply(profile, Automation.BackupDirectory); });
                rows.Add(new("Aplicar perfil completo Elgin", printer.Name, "Padrão global e conta executando o RBT", "Aplicado", "DEVMODE completo relido e conferido, incluindo dados privados do driver. Papel/corte/logo do modelo conferidos manualmente ao capturar. Backup=" + backup + ". Logo gravado na memória física da impressora e impressão real precisam de conferência. Preferências antigas de outros usuários existentes podem prevalecer no perfil deles."));
            } catch (Exception e) { rows.Add(new("Aplicar perfil completo Elgin", printer.Name, "Fila selecionada", "Erro", e.Message)); }
        }
        return new("Perfil completo de comprovantes", rows.Any(r => r.Failed) ? "Concluído com falhas" : "Perfil aplicado; teste físico pendente", new() { ["Etapas"] = rows.ToArray(), ["Limite"] = "Não envia imagem à memória da impressora, não altera porta/nome/compartilhamento/padrão nem apaga trabalhos. Sem afirmação de impressão física aprovada." }, DateTimeOffset.Now);
    }
}
