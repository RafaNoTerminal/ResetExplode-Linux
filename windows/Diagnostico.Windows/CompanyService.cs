using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
namespace Diagnostico.Windows;

internal static class CompanyService
{
    internal static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Empresa", "rbt-empresa.json");
    internal static CompanyProfile Load()
    {
        if (!File.Exists(ProfilePath)) return CompanyProfile.Empty;
        var p = JsonSerializer.Deserialize<CompanyProfile>(File.ReadAllText(ProfilePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("Perfil da empresa inválido."); p.Validate(); return p;
    }
    internal static void Save(CompanyProfile p)
    {
        p.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        if (File.Exists(ProfilePath)) File.Copy(ProfilePath, ProfilePath + ".antes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: false);
        File.WriteAllText(ProfilePath, Reports.Json(p));
    }
    internal static void Connect(IntPtr window, CompanyProfile p)
    {
        p.Validate(); var resource = new NetworkResource { Type = 1, RemoteName = p.Share };
        int result = WNetAddConnection3(window, ref resource, null, null, 0x1C); // Temporary + native interactive credential prompt.
        if (result != 0) throw new Win32Exception(result, result == 1219 ? "Já existe conexão com outra conta para esse servidor. Confira as sessões existentes; o RBT não as desconecta automaticamente." : new Win32Exception(result).Message);
        if (!Directory.Exists(p.Root)) throw new IOException("Conexão solicitada, mas a pasta Helpdesk não foi acessível. Confira caminho e permissão da conta.");
    }
    internal static async Task<JsonDocument> Script(string script, CancellationToken ct)
    {
        var output = await MainForm.UpdateProcess(script, ct);
        if (output.ExitCode != 0) throw new IOException(ActivationFlow.Clean(output.Error) + "\n" + output.Code);
        return JsonDocument.Parse(output.Output);
    }
    internal static async Task<TestResult> ApplyPrinters(string script, string name)
    {
        using var data = await Script(script, CancellationToken.None); var rows = OperationResults.Packages(data.RootElement.GetRawText());
        return new(name, rows.Any(r => r.Failed) ? "Concluído com falhas" : rows.Count == 0 ? "Nenhuma fila correspondente" : rows.Any(r => r.Estado == "Pendente") ? "Concluído com pendências; conferir detalhes" : "Concluído", new() { ["Etapas"] = rows.ToArray() }, DateTimeOffset.Now);
    }
    internal static async Task<TestResult> RunTool(CompanyProfile profile, CompanyTool tool)
    {
        profile.Validate(); string source = profile.Resolve(tool.RelativePath);
        if (tool.Kind is CompanyToolKind.Folder or CompanyToolKind.DriverFolder or CompanyToolKind.Manual) {
            if (tool.Kind == CompanyToolKind.Manual && !new[] { ".pdf", ".txt", ".md", ".html" }.Contains(Path.GetExtension(source).ToLowerInvariant())) throw new IOException("Extensão não permitida para manual.");
            Process.Start(new ProcessStartInfo(source) { UseShellExecute = true });
            return new(tool.Title, "Abertura solicitada", new() { ["Item"] = tool.Title, ["Observação"] = "Abertura de material/pasta; nenhum driver ou ajuste foi aplicado automaticamente." }, DateTimeOffset.Now);
        }
        string extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".exe" or ".msi")) throw new IOException("Somente instaladores EXE/MSI podem ser executados por este fluxo.");
        var directory = ActivationService.PrivateDirectory();
        try {
            string local = Path.Combine(directory.FullName, "installer" + extension);
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var output = new FileStream(local, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await input.CopyToAsync(output);
            string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(local));
            var check = await MainForm.UpdateProcess("$ErrorActionPreference='Stop';$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "'));$s=Get-AuthenticodeSignature -LiteralPath $p;if($s.Status -ne 'Valid'){throw ('Assinatura não válida: '+$s.Status)};$s.SignerCertificate.Subject", CancellationToken.None);
            if (check.ExitCode != 0) throw new IOException("Instalador não executado: assinatura digital não foi confirmada. " + check.Error);
            string hash; await using (var stream = File.OpenRead(local)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            ProcessStartInfo start;
            if (extension == ".msi") {
                start = new(Path.Combine(Environment.SystemDirectory, "msiexec.exe")) { UseShellExecute = false };
                foreach (string arg in new[] { "/i", local, "/passive", "/norestart" }) start.ArgumentList.Add(arg);
            } else start = new(local) { UseShellExecute = true };
            using var process = Process.Start(start) ?? throw new IOException("Instalador não iniciou."); await process.WaitForExitAsync();
            return new(tool.Title, process.ExitCode is 0 or 3010 ? "Instalador concluiu; conferir aplicativo" : "Falha no instalador", new() { ["Código"] = process.ExitCode, ["Reinício"] = process.ExitCode == 3010 ? "Necessário, somente manual" : "Nenhum solicitado pelo RBT", ["Assinante"] = check.Output, ["SHA256"] = hash, ["Limite"] = "Código de saída do instalador não confirma sozinho configuração/login/funcionamento do aplicativo." }, DateTimeOffset.Now);
        } finally { try { directory.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct NetworkResource { public int Scope, Type, DisplayType, Usage; public string? LocalName, RemoteName, Comment, Provider; }
    [DllImport("mpr.dll", EntryPoint = "WNetAddConnection3W", CharSet = CharSet.Unicode)] static extern int WNetAddConnection3(IntPtr hwnd, ref NetworkResource resource, string? password, string? user, uint flags);
}
