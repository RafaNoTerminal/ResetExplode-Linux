using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Diagnostico.Windows;

internal static class SmokeChecks
{
    internal static void VerifyAdministratorManifest()
    {
        if (!Automation.Admin) throw new Exception("O aplicativo não está elevado.");
        var module = GetModuleHandle(null); var resource = FindResource(module, (IntPtr)1, (IntPtr)24);
        if (resource == IntPtr.Zero) throw new Exception("Manifesto não foi incorporado ao executável.");
        var size = SizeofResource(module, resource); var bytes = new byte[size]; Marshal.Copy(LockResource(LoadResource(module, resource)), bytes, 0, (int)size);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(bytes).Trim('\uFEFF', '\0', ' ', '\r', '\n', '\t'));
        if (!xml.Descendants().Any(e => e.Name.LocalName == "requestedExecutionLevel" && (string?)e.Attribute("level") == "requireAdministrator")) throw new Exception("Manifesto não exige administrador.");
    }
    internal static void VerifyPackageFailures()
    {
        // Shadow cmdlets only inside this fixture process. Never remove real packages.
        string fixture = """
            $script:attempts=[System.Collections.Generic.List[string]]::new()
            $script:removed=$false
            function Get-AppxPackage { param($Name,$ErrorAction)
              @([pscustomobject]@{Name='Microsoft.XboxGameCallableUI';PackageFullName='protected_fixture';NonRemovable=$true},[pscustomobject]@{Name='Microsoft.XboxApp';PackageFullName='failed_fixture';NonRemovable=$false})
              if(-not $script:removed){[pscustomobject]@{Name='Microsoft.GetHelp';PackageFullName='removed_fixture';NonRemovable=$false}}
            }
            function Remove-AppxPackage { param($Package,$ErrorAction)
              $script:attempts.Add($Package)
              if($Package -eq 'removed_fixture'){$script:removed=$true;return}
              throw 'Acesso negado fixture 0x80070005'
            }
            """;
        string script = fixture + "\n$result = & {\n" + AutomationCatalog.PackageScript(AutomationCatalog.Select(["app:Microsoft.Xbox*", "app:Microsoft.GetHelp"]), apply: true) + "\n}\n" + """
            $data=$result|ConvertFrom-Json
            if(@($data|Where-Object {$_.Estado -eq 'Preservado'}).Count -ne 1){throw 'Protegido sem classificação correta'}
            if(@($data|Where-Object {$_.Estado -eq 'Erro' -and $_.Detalhes -like '*0x80070005*'}).Count -ne 1){throw 'Falha sem mensagem completa'}
            if(@($data|Where-Object {$_.Estado -eq 'Removido'}).Count -ne 1){throw 'App removido ausente do histórico'}
            if($script:attempts.Count -ne 2 -or $script:attempts.Contains('protected_fixture')){throw 'Tentou remover componente protegido'}
            """;
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new Exception("Fixture Appx excedeu tempo."); }
        if (process.ExitCode != 0) throw new Exception("Fixture Appx: " + error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
    }
    internal static void VerifyNativeExitCode()
    {
        var outcome = Task.Run(() => Automation.CaptureProcess(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "exit", "/b", "-2147219813"], CancellationToken.None)).GetAwaiter().GetResult();
        if (outcome.ExitCode != -2147219813 || !outcome.Code.Contains("0x8004069B")) throw new Exception("Código nativo do processo foi convertido incorretamente.");
        string signed = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!Task.Run(() => OneDriveDetection.MicrosoftSigned(signed)).GetAwaiter().GetResult()) throw new Exception("Assinatura Microsoft válida não foi reconhecida: " + OneDriveDetection.SignatureDetails);
        string unsigned = Path.Combine(Path.GetTempPath(), "onedrive-signature-fixture-" + Guid.NewGuid().ToString("N") + ".exe");
        try {
            File.WriteAllText(unsigned, "Signature check fixture, not an executable.");
            if (Task.Run(() => OneDriveDetection.MicrosoftSigned(unsigned)).GetAwaiter().GetResult()) throw new Exception("Arquivo sem assinatura foi aceito.");
        } finally { File.Delete(unsigned); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
}
