using System.Security.Cryptography;
using System.Text.Json;
namespace Diagnostico.Windows;

internal static class RicohSetupService
{
    internal static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Empresa", "rbt-ricoh-driver.json");
    internal static LocalPrinterPackage Load() {
        if (!File.Exists(ProfilePath)) throw new IOException("Perfil do driver Ricoh ausente. Mantenha a pasta Empresa e o pacote DRIVERS junto do executável.");
        var p = JsonSerializer.Deserialize<LocalPrinterPackage>(File.ReadAllText(ProfilePath)) ?? throw new IOException("Perfil do driver Ricoh inválido."); p.Validate(); return p;
    }
    internal static async Task<TestResult> Install(LocalPrinterPackage package, string model) {
        package.Validate(); if (!package.Models.Contains(model)) throw new IOException("Modelo fora do pacote Ricoh.");
        // If the exact selected model already exists, preserve it even on a PC without the USB package.
        using (var data = await CompanyService.Script(PrinterScripts.Inventory, CancellationToken.None)) {
            if (data.RootElement.GetProperty("Drivers").EnumerateArray().Any(d => d.GetProperty("Name").GetString()==model && d.GetProperty("PrinterEnvironment").GetString()=="Windows x64")) return new("Driver Ricoh", "Driver já disponível", new() { ["Etapas"] = new[] { new OperationRow("Instalar driver Ricoh", model, "Computador", "Preservado", "Modelo já registrado no spooler x64; sem reinstalar/atualizar ou alterar filas existentes.") } }, DateTimeOffset.Now);
        }
        string source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, package.RelativeDirectory.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar)));
        string allowed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"DRIVERS"))+Path.DirectorySeparatorChar;
        if (!source.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)) throw new IOException("Pacote fora da pasta DRIVERS.");
        for (var parent=new DirectoryInfo(source);parent is not null && parent.FullName.StartsWith(allowed,StringComparison.OrdinalIgnoreCase);parent=parent.Parent) if((parent.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Links de diretório não são aceitos no pacote.");
        var directory = ActivationService.PrivateDirectory();
        try {
            foreach (string file in Directory.EnumerateFiles(source)) {
                if ((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0) throw new IOException("Pacote contém link de arquivo; preservado para revisão.");
                await using var input = File.OpenRead(file); await using var output = new FileStream(Path.Combine(directory.FullName,Path.GetFileName(file)),FileMode.CreateNew,FileAccess.Write,FileShare.None); await input.CopyToAsync(output);
            }
            string inf = Path.Combine(directory.FullName,package.Inf), catalog=Path.Combine(directory.FullName,package.Catalog);
            foreach(var (file,expected) in new[] {(inf,package.InfSha256),(catalog,package.CatalogSha256)}) {
                await using var input=File.OpenRead(file); if(!Convert.ToHexString(await SHA256.HashDataAsync(input)).Equals(expected,StringComparison.OrdinalIgnoreCase)) throw new IOException("INF/catálogo difere do pacote Ricoh fornecido; instalação não iniciada.");
            }
            var result=await CompanyService.ApplyPrinters(package.InstallScript(inf,model),"Instalar driver Ricoh");
            result.Dados["Pacote"]=package.Title; result.Dados["SHA256 INF"]=package.InfSha256; result.Dados["Observação"]="PNPUtil valida a assinatura e os arquivos do pacote pela política do Windows. Sem importar certificados, enfraquecer políticas, reiniciar spooler ou atualizar filas existentes."; return result;
        } finally { try{directory.Delete(true);}catch(IOException){}catch(UnauthorizedAccessException){} }
    }
    internal static bool Ready(TestResult result) { var rows=OperationResults.Read(result); return rows.Count>0 && !rows.Any(r=>r.Failed||r.Estado=="Pendente") && rows.Any(r=>r.Estado is "Aplicado" or "Preservado"); }
    internal static async Task<TestResult> InstallQueues(LocalPrinterPackage package,string model,CompanyPrinter[] selected) {
        if(selected.Length==0||selected.Any(p=>p.Receipt))throw new IOException("Marque somente filas Ricoh compatíveis com o modelo escolhido.");
        var driver=await Install(package,model);if(!Ready(driver))return driver;
        using var before=await CompanyService.Script(PrinterScripts.Inventory,CancellationToken.None);Directory.CreateDirectory(Automation.BackupDirectory);
        string file=Path.Combine(Automation.BackupDirectory,"ricoh-inventario-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");await File.WriteAllTextAsync(file,before.RootElement.GetRawText());
        var installed=await CompanyService.ApplyPrinters(PrinterScripts.Install(selected,model),"Instalar filas Ricoh");
        var rows=OperationResults.Read(driver).Concat(OperationResults.Read(installed)).ToArray();
        return new("Ricoh · driver e filas",rows.Any(r=>r.Failed)?"Concluído com falhas":"Driver e filas confirmados; teste de impressão pendente",new(){["Etapas"]=rows,["Inventário anterior"]=file,["Limite"]="Filas existentes, driver já em uso, preferências, portas, trabalhos e padrão preservados. Instalação não comprova impressão física."},DateTimeOffset.Now);
    }
}
