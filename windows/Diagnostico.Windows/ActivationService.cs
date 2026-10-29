using System.Diagnostics;
using System.Management;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
namespace Diagnostico.Windows;

internal static class ActivationService
{
    internal static ActivationSnapshot Read()
    {
        var errors = new List<string>(); var products = new List<LicenseProduct>(); string caption = "Windows", edition = ""; int build = Environment.OSVersion.Version.Build; bool client = false, office = false;
        try {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"); edition = Convert.ToString(key?.GetValue("EditionID")) ?? "";
            using var query = new ManagementObjectSearcher("SELECT Caption,ProductType FROM Win32_OperatingSystem"); using var values = query.Get();
            foreach (ManagementObject item in values) using (item) { caption = Convert.ToString(item["Caption"]) ?? caption; client = Convert.ToInt32(item["ProductType"]) == 1; }
        } catch (Exception e) { errors.Add("Windows: " + ActivationFlow.Clean(e.Message)); }
        try {
            using var query = new ManagementObjectSearcher("SELECT Name,ApplicationID,LicenseFamily,LicenseStatus,LicenseIsAddon FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL"); using var values = query.Get();
            foreach (ManagementObject item in values) using (item) {
                string app = (Convert.ToString(item["ApplicationID"]) ?? "").ToLowerInvariant();
                if (app != "55c92734-d682-4d71-983e-d6ec3f16059f" && app != "0ff1ce15-a989-479d-af46-f275c6370663" && app != "59a52881-a989-479d-af46-f275c6370663") continue;
                if (app == "55c92734-d682-4d71-983e-d6ec3f16059f" && Convert.ToBoolean(item["LicenseIsAddon"])) continue;
                products.Add(new(Convert.ToString(item["Name"]) ?? "", app, Convert.ToString(item["LicenseFamily"]) ?? "", Convert.ToInt32(item["LicenseStatus"])));
                if (app != "55c92734-d682-4d71-983e-d6ec3f16059f") office = true;
            }
        } catch (Exception e) { errors.Add("Licenciamento WMI: " + ActivationFlow.Clean(e.Message)); }
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
            try {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var c2r = root.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration");
                office |= !string.IsNullOrWhiteSpace(Convert.ToString(c2r?.GetValue("InstallationPath")));
                foreach (string version in new[] { "14.0", "15.0", "16.0" }) {
                    using var msi = root.OpenSubKey(@"SOFTWARE\Microsoft\Office\" + version + @"\Common\InstallRoot");
                    office |= !string.IsNullOrWhiteSpace(Convert.ToString(msi?.GetValue("Path")));
                }
            } catch (Exception e) { errors.Add("Detecção Office: " + ActivationFlow.Clean(e.Message)); }
        }
        var windows = products.Where(p => p.Application == "55c92734-d682-4d71-983e-d6ec3f16059f" && p.Family.Equals(edition, StringComparison.OrdinalIgnoreCase)).ToArray();
        bool? licensed = windows.Length == 1 ? windows[0].Status == 1 : null;
        var opened = new List<string>();
        var names = new HashSet<string>(["WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "MSACCESS", "MSPUB", "ONENOTE", "VISIO", "WINPROJ", "GROOVE", "LYNC", "LIME"], StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses()) using (process) { try { if (names.Contains(process.ProcessName)) opened.Add(process.ProcessName); } catch (InvalidOperationException) { } }
        return new(caption, edition, build, client, licensed, office, opened.Distinct().ToArray(), products.ToArray(), errors.ToArray());
    }
    internal static byte[] EngineBytes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RBT.MAS_AIO.cmd") ?? throw new IOException("Motor MAS ausente do executável.");
        using var memory = new MemoryStream(); stream.CopyTo(memory); var bytes = memory.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(ActivationFlow.EngineHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("SHA256 do motor não corresponde à versão conferida. Nenhum script será executado.");
        return bytes;
    }
    internal static DirectoryInfo PrivateDirectory()
    {
        var access = new DirectorySecurity(); access.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            access.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "RBT-activation-" + Guid.NewGuid().ToString("N"));
        var directory = new DirectoryInfo(path); directory.Create(access); return directory;
    }
    internal static async Task<ActivationRun> Run(ActivationMethod method, IProgress<string> progress)
    {
        if (!Automation.Admin) throw new UnauthorizedAccessException("Administrador necessário.");
        byte[] bytes = EngineBytes(); var directory = PrivateDirectory();
        try {
            string file = Path.Combine(directory.FullName, "MAS_AIO.cmd"); await File.WriteAllBytesAsync(file, bytes);
            if (!Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))).Equals(ActivationFlow.EngineHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("O arquivo extraído não passou na conferência SHA256.");
            return await RunProcess(directory.FullName, method, progress);
        } finally { try { directory.Delete(recursive: true); } catch (IOException) { progress.Report("Não foi possível limpar a pasta temporária: " + directory.FullName); } catch (UnauthorizedAccessException) { progress.Report("Pasta temporária permaneceu protegida: " + directory.FullName); } }
    }
    // No user text is passed to cmd.exe. The engine filename and every switch are fixed.
    internal static async Task<ActivationRun> RunProcess(string directory, ActivationMethod method, IProgress<string> progress)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) {
            Arguments = "/d /q /c MAS_AIO.cmd " + ActivationFlow.Arguments(method), WorkingDirectory = directory,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["PSModulePath"] = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "Modules") + ";" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsPowerShell", "Modules");
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o motor."); process.StandardInput.Close();
        async Task<string> Read(StreamReader reader) {
            var buffer = new StringBuilder();
            while (await reader.ReadLineAsync() is { } line) { string text = ActivationFlow.Clean(line); progress.Report(text); if (buffer.Length < 1048576) buffer.AppendLine(text); }
            return buffer.ToString();
        }
        var output = Read(process.StandardOutput); var error = Read(process.StandardError);
        // Licensing transactions must finish; closing the UI never kills the engine mid-change.
        await process.WaitForExitAsync(); return new(process.ExitCode, await output, await error);
    }
}
