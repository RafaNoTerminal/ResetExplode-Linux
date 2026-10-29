using System.Text;
using System.Text.Json;
namespace Diagnostico;

public record UpdateChoice(string Id, int Revision, string Title, bool Recommended, double MiB, string License)
{
    public override string ToString() => Title + " · " + MiB.ToString("N0") + " MiB" + (Recommended ? " · recomendada" : " · opcional");
}
public static class WindowsUpdateScript
{
    public static string Build(UpdateChoice[]? install = null)
    {
        if (install is not null && (install.Length == 0 || install.Any(u => !Guid.TryParseExact(u.Id, "D", out _) || u.Revision < 0))) throw new ArgumentException("Seleção de atualização inválida.");
        string selection = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(install ?? [])));
        return "$apply=$" + (install is not null ? "true" : "false") + ";$wanted=([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + selection + "'))|ConvertFrom-Json)\n" + Body;
    }
    public const string Body = """
        $ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
        [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        $session=New-Object -ComObject Microsoft.Update.Session
        $session.ClientApplicationID='RBT Diagnostico'
        $search=$session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
        if([int]$search.ResultCode -ne 2){throw ('Busca incompleta do Windows Update. ResultCode='+$search.ResultCode)}
        $rows=[Collections.Generic.List[object]]::new()
        if(-not $apply){
          foreach($u in $search.Updates){
            if($u.InstallationBehavior.CanRequestUserInput){continue}
            $rows.Add([pscustomobject]@{Id=$u.Identity.UpdateID;Revision=$u.Identity.RevisionNumber;Title=$u.Title;Recommended=[bool](-not $u.BrowseOnly -and $u.AutoSelectOnWebSites);MiB=[math]::Round($u.MaxDownloadSize/1MB,1);License=[string]$u.EulaText})
          }
          ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 5 -Compress;return
        }
        $system=New-Object -ComObject Microsoft.Update.SystemInfo
        if($system.RebootRequired){throw 'Já há reinício pendente. Reinicie manualmente e consulte novamente antes de instalar.'}
        foreach($choice in $wanted){
          $u=$null
          foreach($candidate in $search.Updates){if($candidate.Identity.UpdateID -eq $choice.Id -and $candidate.Identity.RevisionNumber -eq $choice.Revision){$u=$candidate;break}}
          try{
            if($null -eq $u){throw 'A atualização mudou ou não está mais pendente. Consulte novamente.'}
            if($u.InstallationBehavior.CanRequestUserInput){throw 'Atualização interativa; use Windows Update.'}
            if(-not $u.EulaAccepted){$u.AcceptEula()}
            $collection=New-Object -ComObject Microsoft.Update.UpdateColl;[void]$collection.Add($u)
            if(-not $u.IsDownloaded){
              $downloader=$session.CreateUpdateDownloader();$downloader.Updates=$collection
              $download=$downloader.Download();$dr=$download.GetUpdateResult(0)
              if([int]$dr.ResultCode -ne 2 -or -not $u.IsDownloaded){throw ('Download falhou. ResultCode='+$dr.ResultCode+' HRESULT='+('0x{0:X8}' -f ([int]$dr.HResult)))}
            }
            $installer=$session.CreateUpdateInstaller();$installer.Updates=$collection
            $result=$installer.Install();$item=$result.GetUpdateResult(0)
            $ok=([int]$item.ResultCode -eq 2)
            $rows.Add([pscustomobject]@{Acao='Instalar atualização';Pacote=$choice.Title;Escopo='Computador';Estado=$(if($ok){'Aplicado'}else{'Erro'});Detalhes=('ResultCode='+$item.ResultCode+' HRESULT='+('0x{0:X8}' -f ([int]$item.HResult))+'; reinício necessário='+$result.RebootRequired)})
            if($result.RebootRequired){
              foreach($remaining in $wanted){if(-not @($rows|Where-Object {$_.Pacote -eq $remaining.Title}).Count){$rows.Add([pscustomobject]@{Acao='Instalar atualização';Pacote=$remaining.Title;Escopo='Computador';Estado='Pendente';Detalhes='Reinicie manualmente e consulte novamente para continuar.'})}}
              break
            }
          }catch{$rows.Add([pscustomobject]@{Acao='Instalar atualização';Pacote=$choice.Title;Escopo='Computador';Estado='Erro';Detalhes=$_.Exception.Message})}
        }
        ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 5 -Compress
        """;
}
