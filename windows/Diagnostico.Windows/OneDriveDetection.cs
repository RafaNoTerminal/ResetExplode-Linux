using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Diagnostico.Windows;

internal static class OneDriveDetection
{
    internal static OneDriveSnapshot Read()
    {
        var registrations = new List<OneDriveRegistration>(); var errors = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }) {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
                try {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    foreach (string name in uninstall.GetSubKeyNames()) {
                        try {
                            using var key = uninstall.OpenSubKey(name); if (key is null) continue;
                            string display = Convert.ToString(key.GetValue("DisplayName")) ?? "";
                            if (!display.Equals("Microsoft OneDrive", StringComparison.OrdinalIgnoreCase) && !name.Equals("OneDriveSetup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                            string command = Environment.ExpandEnvironmentVariables(Convert.ToString(key.GetValue("UninstallString")) ?? "");
                            registrations.Add(new(command, hive == RegistryHive.CurrentUser ? "Perfil atual" : "Computador · todos os usuários", hive + "\\" + view + "\\" + uninstall.Name + "\\" + name));
                        } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) {
                            if (name.Contains("OneDrive", StringComparison.OrdinalIgnoreCase)) errors.Add(name + ": " + e.Message);
                        }
                    }
                } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { errors.Add(hive + "/" + view + ": " + e.Message); }
            }
        }
        var clients = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive", "OneDrive.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft OneDrive", "OneDrive.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft OneDrive", "OneDrive.exe")
        }.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();
        return new(registrations.ToArray(), clients, errors.ToArray());
    }
    internal static async Task<bool> MicrosoftSigned(string file)
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(file));
        string script = "$ErrorActionPreference='Stop';$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "'));$s=Get-AuthenticodeSignature -LiteralPath $p;[bool]($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match '(?i)(^|,)\\s*O=\"?Microsoft Corporation\"?(,|$)')|ConvertTo-Json -Compress";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try {
            var outcome = await Automation.CaptureProcess(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], timeout.Token);
            return outcome.ExitCode == 0 && JsonSerializer.Deserialize<bool>(outcome.Output.Trim());
        } catch (OperationCanceledException) { return false; }
    }
    internal static Task<OperationRow> Uninstall(CancellationToken ct) => OneDriveUninstall.Execute(
        () => Task.Run(Read), File.Exists, MicrosoftSigned,
        plan => Automation.CaptureProcess(plan.Executable!, plan.Arguments, CancellationToken.None), ct);
}
