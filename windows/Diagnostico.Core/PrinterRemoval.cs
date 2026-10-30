using System.Text;
using System.Text.Json;
namespace Diagnostico;
public record InstalledPrinterChoice(string Name,string Driver,string Port,bool Default)
{
    public override string ToString()=>Name+" · "+Driver+" · "+Port+(Default?" · PADRÃO":"");
}
public static class PrinterRemoval
{
    public static string Build(InstalledPrinterChoice[] selected,string backupFile,bool allowDefault) {
        if(selected.Length==0||selected.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=selected.Length||selected.Any(p=>string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>512||p.Name.Any(char.IsControl)||string.IsNullOrWhiteSpace(p.Driver)||string.IsNullOrWhiteSpace(p.Port)))throw new ArgumentException("Selecione impressoras instaladas válidas, sem duplicatas.");
        var data=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Selected=selected,Backup=backupFile,AllowDefault=allowDefault})));
        return "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$data=([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+data+"'))|ConvertFrom-Json)\n"+Body;
    }
    public const string Body="""
        $rows=[Collections.Generic.List[object]]::new();$snapshots=[Collections.Generic.List[object]]::new()
        $originalDefault=@(Get-CimInstance Win32_Printer|Where-Object Default|Select-Object -ExpandProperty Name)
        foreach($p in $data.Selected){
          $found=@(Get-Printer|Where-Object {$_.Name -eq $p.Name})
          if($found.Count -eq 1){
            $ticket=$null;$note=''
            try{$ticket=(Get-PrintConfiguration -PrinterName $p.Name -ErrorAction Stop).PrintTicketXML}catch{$note=$_.Exception.Message}
            $snapshots.Add([pscustomobject]@{Name=$found[0].Name;Driver=$found[0].DriverName;Port=$found[0].PortName;Shared=$found[0].Shared;ShareName=$found[0].ShareName;PrintTicket=$ticket;PreferenceNote=$note})
          }
        }
        [pscustomobject]@{Computer=$env:COMPUTERNAME;At=[DateTime]::Now;Default=$originalDefault;Selected=@($snapshots.ToArray());Limit='Inventário e ticket quando exposto; não inclui toda preferência privada nem restauração automática.'}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $data.Backup -Encoding UTF8
        foreach($p in $data.Selected){try{
          $found=@(Get-Printer|Where-Object {$_.Name -eq $p.Name})
          if($found.Count -eq 0){$rows.Add([pscustomobject]@{Acao='Excluir impressora';Pacote=$p.Name;Escopo='Computador/conta atual';Estado='Ausente';Detalhes='Já não está instalada; nenhuma exclusão executada.'});continue}
          if($found.Count -ne 1 -or $found[0].DriverName -ne $p.Driver -or $found[0].PortName -ne $p.Port){throw 'Fila mudou desde a seleção. Atualize a lista e revise antes de excluir.'}
          $currentDefault=@(Get-CimInstance Win32_Printer|Where-Object Default|Select-Object -ExpandProperty Name)
          if($p.Name -in $currentDefault -and -not $data.AllowDefault){throw 'Impressora padrão não teve exclusão autorizada; preservada.'}
          if(@(Get-PrintJob -PrinterName $p.Name -ErrorAction Stop).Count){throw 'Há trabalhos pendentes. Aguarde terminar; o app não cancela trabalhos para excluir.'}
          # InputObject is the exact queried queue, avoiding wildcard expansion in -Name.
          Remove-Printer -InputObject $found[0] -Confirm:$false -ErrorAction Stop
          if(@(Get-Printer|Where-Object {$_.Name -eq $p.Name}).Count){throw 'A fila continua presente após a solicitação de exclusão.'}
          $rows.Add([pscustomobject]@{Acao='Excluir impressora';Pacote=$p.Name;Escopo='Computador/conta atual';Estado='Removido';Detalhes=('Fila ausente após excluir. Driver e porta preservados. Backup='+$data.Backup)})
        }catch{$rows.Add([pscustomobject]@{Acao='Excluir impressora';Pacote=$p.Name;Escopo='Computador/conta atual';Estado='Erro';Detalhes=($_|Format-List * -Force|Out-String)})}}
        if($originalDefault.Count -eq 1){
          $survivor=@(Get-CimInstance Win32_Printer|Where-Object {$_.Name -eq $originalDefault[0]})
          if($survivor.Count -eq 1 -and -not $survivor[0].Default){
            $restored=Invoke-CimMethod -InputObject $survivor[0] -MethodName SetDefaultPrinter
            if($restored.ReturnValue -ne 0){$rows.Add([pscustomobject]@{Acao='Preservar impressora padrão';Pacote=$originalDefault[0];Escopo='Conta atual';Estado='Erro';Detalhes=('Código='+$restored.ReturnValue)})}
          }
        }
        ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 5 -Compress
        """;
}
