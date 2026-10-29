using System.Text;
using System.Text.Json;
namespace Diagnostico.Windows;

internal static class MaintenanceSmoke
{
    internal static void Verify()
    {
        _ = DeviceDetection.Read();
        if (!UsbHubPower.RootHub(@"USB\ROOT_HUB30\fixture_0") || UsbHubPower.RootHub(@"USB\VID_1234\keyboard") || UsbHubPower.RootHub(@"PCI\fixture")) throw new Exception("Filtro de hubs USB aceitou outro dispositivo.");
        foreach (var screen in Screen.AllScreens) {
            var current = DisplaySettings.Current(screen.DeviceName);
            if (current.Bytes.Length != 220 || current.Width <= 0 || current.Height <= 0 || current.Hz <= 0) throw new Exception("DEVMODE inválido.");
            if (DisplaySettings.Modes(screen.DeviceName).Count == 0) throw new Exception("Modos de tela ausentes.");
            DisplaySettings.Apply(current, 2); // Only CDS_TEST: never change a CI display.
        }
        var choices = new[] { new UpdateChoice("00000000-0000-0000-0000-000000000001", 1, "Fixture one", true, 1, "Terms"), new UpdateChoice("00000000-0000-0000-0000-000000000002", 1, "Fixture two", true, 2, "Terms") };
        string mock = """
            $script:items=@(
              [pscustomobject]@{Identity=[pscustomobject]@{UpdateID='00000000-0000-0000-0000-000000000001';RevisionNumber=1};Title='Fixture one';BrowseOnly=$false;AutoSelectOnWebSites=$true;MaxDownloadSize=1MB;EulaText='Terms';EulaAccepted=$true;IsDownloaded=$true;InstallationBehavior=[pscustomobject]@{CanRequestUserInput=$false}},
              [pscustomobject]@{Identity=[pscustomobject]@{UpdateID='00000000-0000-0000-0000-000000000002';RevisionNumber=1};Title='Fixture two';BrowseOnly=$false;AutoSelectOnWebSites=$true;MaxDownloadSize=2MB;EulaText='Terms';EulaAccepted=$true;IsDownloaded=$true;InstallationBehavior=[pscustomobject]@{CanRequestUserInput=$false}}
            )
            function New-Object { param([string]$ComObject)
              switch($ComObject){
                'Microsoft.Update.SystemInfo' {return [pscustomobject]@{RebootRequired=$false}}
                'Microsoft.Update.UpdateColl' {return ,([Collections.Generic.List[object]]::new())}
                'Microsoft.Update.Session' {
                  $session=[pscustomobject]@{ClientApplicationID=''}
                  $session|Add-Member ScriptMethod CreateUpdateSearcher {
                    $search=[pscustomobject]@{}
                    $search|Add-Member ScriptMethod Search {param($criteria);return [pscustomobject]@{ResultCode=2;Updates=$script:items}}
                    return $search
                  }
                  $session|Add-Member ScriptMethod CreateUpdateInstaller {
                    $installer=[pscustomobject]@{Updates=$null}
                    $installer|Add-Member ScriptMethod Install {
                      $result=[pscustomobject]@{RebootRequired=$true}
                      $result|Add-Member ScriptMethod GetUpdateResult {param($index);return [pscustomobject]@{ResultCode=2;HResult=0}}
                      return $result
                    }
                    return $installer
                  }
                  return $session
                }
                default {throw 'Unexpected COM request in fixture'}
              }
            }
            """;
        ProcessOutcome Execute(string script) => Task.Run(() => MainForm.UpdateProcess(script, CancellationToken.None)).GetAwaiter().GetResult();
        var scan = Execute(mock + "\n" + WindowsUpdateScript.Build());
        if (scan.ExitCode != 0 || JsonSerializer.Deserialize<UpdateChoice[]>(scan.Output)?.Length != 2) throw new Exception("Windows Update fixture busca: " + scan.Error + scan.Output);
        var install = Execute(mock + "\n" + WindowsUpdateScript.Build(choices));
        if (install.ExitCode != 0) throw new Exception("Windows Update fixture instalação: " + install.Error);
        var rows = OperationResults.Packages(install.Output);
        if (rows.Count != 2 || rows[0].Estado != "Aplicado" || rows[1].Estado != "Pendente") throw new Exception("Windows Update perdeu sucesso por item ou continuou após reinício pendente: " + install.Output);
        // Parse the real production script, but execute COM calls only through the fixture above.
        string code = Convert.ToBase64String(Encoding.UTF8.GetBytes(WindowsUpdateScript.Build(choices)));
        var parse = Execute("$t=$null;$e=$null;[System.Management.Automation.Language.Parser]::ParseInput([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + code + "')),[ref]$t,[ref]$e)|Out-Null;if($e.Count){throw ($e|Out-String)}");
        if (parse.ExitCode != 0) throw new Exception("Windows Update sintaxe: " + parse.Error);
    }
}
