using System.Text;
using System.Text.Json;
namespace Diagnostico;

public static class PrinterScripts
{
    static string Prefix(object? value = null) => "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';$data=([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))) + "'))|ConvertFrom-Json)\n";
    public static string Inventory => Prefix() + """
        $spooler=Get-Service Spooler
        $printers=@(Get-Printer|Select-Object Name,DriverName,PortName,Shared,ShareName,PrinterStatus,Type)
        $drivers=@(Get-PrinterDriver|Select-Object Name,Manufacturer,MajorVersion,PrinterEnvironment)
        $ports=@(Get-PrinterPort|Select-Object Name,PrinterHostAddress,PortNumber)
        $default=@(Get-CimInstance Win32_Printer|Where-Object Default|Select-Object -ExpandProperty Name)
        [pscustomobject]@{Printers=$printers;Drivers=$drivers;Ports=$ports;Default=$default;Spooler=[string]$spooler.Status}|ConvertTo-Json -Depth 6 -Compress
        """;
    public static string Install(CompanyPrinter[] printers, string driver)
    {
        if (printers.Length == 0 || string.IsNullOrWhiteSpace(driver)) throw new ArgumentException("Selecione filas e um driver instalado.");
        new CompanyProfile(@"\\fixture\share", printers, []).Validate();
        return Prefix(new { Printers = printers, Driver = driver }) + """
            if(-not @(Get-PrinterDriver | Where-Object {$_.Name -eq $data.Driver}).Count){throw 'O driver escolhido não está instalado.'}
            if((Get-Service Spooler).Status -ne 'Running'){throw 'Spooler não está iniciado. Use Iniciar Spooler e repita.'}
            $rows=[Collections.Generic.List[object]]::new()
            $default=@(Get-CimInstance Win32_Printer|Where-Object Default|Select-Object -ExpandProperty Name)
            foreach($p in $data.Printers){
              try{
                $existing=@(Get-Printer | Where-Object {$_.Name -eq $p.Name})
                if($existing.Count){
                  $existingPort=Get-PrinterPort | Where-Object {$_.Name -eq $existing[0].PortName}
                  if($existingPort.PrinterHostAddress -ne $p.Ip){throw 'Nome de fila já existe com outro destino. Fila e porta preservadas; confira manualmente.'}
                  $rows.Add([pscustomobject]@{Acao='Instalar impressora';Pacote=$p.Name;Escopo='Computador';Estado='Preservado';Detalhes=('Já existe no destino '+$p.Ip+'; driver e configurações preservados.')});continue
                }
                $portName='RBT_TCP_'+$p.Ip
                $port=@(Get-PrinterPort | Where-Object {$_.Name -eq $portName})
                if($port.Count -and $port[0].PrinterHostAddress -ne $p.Ip){throw 'Porta RBT existente aponta para outro IP; nenhuma porta foi alterada.'}
                if(-not $port.Count){Add-PrinterPort -Name $portName -PrinterHostAddress $p.Ip -PortNumber 9100 | Out-Null}
                Add-Printer -Name $p.Name -DriverName $data.Driver -PortName $portName | Out-Null
                $after=@(Get-Printer|Where-Object {$_.Name -eq $p.Name -and $_.PortName -eq $portName -and $_.DriverName -eq $data.Driver})
                if($after.Count -ne 1){throw 'A fila não foi confirmada após instalar.'}
                $rows.Add([pscustomobject]@{Acao='Instalar impressora';Pacote=$p.Name;Escopo='Computador';Estado='Aplicado';Detalhes=('Fila/porta/driver confirmados. IP='+$p.Ip+'. Impressora padrão não alterada.')})
              }catch{$rows.Add([pscustomobject]@{Acao='Instalar impressora';Pacote=$p.Name;Escopo='Computador';Estado='Erro';Detalhes=$_.Exception.Message})}
            }
            if($default.Count -eq 1){
              $oldDefault=@(Get-CimInstance Win32_Printer|Where-Object {$_.Name -eq $default[0]})
              if($oldDefault.Count -eq 1 -and -not $oldDefault[0].Default){
                $restore=Invoke-CimMethod -InputObject $oldDefault[0] -MethodName SetDefaultPrinter
                if($restore.ReturnValue -ne 0){$rows.Add([pscustomobject]@{Acao='Preservar impressora padrão';Pacote=$default[0];Escopo='Perfil atual';Estado='Erro';Detalhes=('Não foi possível restaurar padrão. Código='+$restore.ReturnValue)})}
              }
            }
            ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 5 -Compress
            """;
    }
    public static string MigrateRicoh(string backupFile, bool restore = false) => Prefix(new { File = backupFile, Restore = restore }) + """
        $old='RICOH SP 377SFNwX PCL 6';$new='PCL6 Driver for Universal Print';$rows=[Collections.Generic.List[object]]::new()
        if(-not $data.Restore){
          if(-not @(Get-PrinterDriver|Where-Object {$_.Name -eq $new}).Count){throw 'Instale primeiro o PCL6 Driver for Universal Print da Ricoh; nenhum driver foi substituído.'}
          $before=@(Get-Printer|Where-Object {$_.DriverName -eq $old}|ForEach-Object {
            $cfg=Get-PrintConfiguration -PrinterName $_.Name -ErrorAction Stop
            [pscustomobject]@{Name=$_.Name;DriverName=$_.DriverName;PortName=$_.PortName;Shared=$_.Shared;ShareName=$_.ShareName;PaperSize=[string]$cfg.PaperSize;DuplexingMode=[string]$cfg.DuplexingMode;Color=[bool]$cfg.Color}
          })
          [pscustomobject]@{Computer=$env:COMPUTERNAME;Printers=$before}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $data.File -Encoding UTF8
        }else{
          $backup=Get-Content -LiteralPath $data.File -Raw|ConvertFrom-Json
          if($backup.Computer -ne $env:COMPUTERNAME){throw 'Backup de outro computador.'}
          $before=@($backup.Printers)
        }
        foreach($p in $before){try{
          $current=@(Get-Printer|Where-Object {$_.Name -eq $p.Name})
          if($current.Count -ne 1 -or $current[0].PortName -ne $p.PortName){throw 'Fila não encontrada ou destino mudou; preservada para revisão.'}
          $target=if($data.Restore){$p.DriverName}else{$new}
          if($target -notin @($old,$new)){throw 'Driver fora do escopo Ricoh permitido.'}
          if(-not @(Get-PrinterDriver|Where-Object {$_.Name -eq $target}).Count){throw 'Driver do destino não está instalado.'}
          if(@(Get-PrintJob -PrinterName $p.Name -ErrorAction Stop).Count){throw 'Fila possui trabalhos pendentes. Aguarde concluir antes de migrar/restaurar o driver.'}
          Set-Printer -Name $p.Name -DriverName $target
          if($p.PaperSize -and $p.DuplexingMode){Set-PrintConfiguration -PrinterName $p.Name -PaperSize $p.PaperSize -DuplexingMode $p.DuplexingMode -Color ([bool]$p.Color)}
          $after=Get-Printer | Where-Object {$_.Name -eq $p.Name}
          if($after.DriverName -ne $target -or $after.PortName -ne $p.PortName -or $after.Shared -ne $p.Shared -or $after.ShareName -ne $p.ShareName){throw 'Conferência detectou diferença; revise fila e restaure o backup.'}
          $rows.Add([pscustomobject]@{Acao='Migrar driver Ricoh';Pacote=$p.Name;Escopo='Fila existente';Estado='Aplicado';Detalhes=('Driver='+$target+'; porta/IP/compartilhamento preservados. Backup='+$data.File)})
        }catch{$rows.Add([pscustomobject]@{Acao='Migrar driver Ricoh';Pacote=$p.Name;Escopo='Fila existente';Estado='Erro';Detalhes=$_.Exception.Message})}}
        ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 5 -Compress
        """;
    public static string Shared(string unc)
    {
        if (CompanyProfile.RootParts(unc).Length != 2) throw new ArgumentException(@"Informe uma fila compartilhada: \\servidor\fila.");
        return Prefix(unc) + "Add-Printer -ConnectionName $data; if(-not @(Get-Printer|Where-Object {$_.Name -eq $data -or ('\\\\'+$_.ComputerName+'\\'+$_.ShareName) -eq $data}).Count){throw 'Windows não confirmou a conexão; atualize o inventário e confira.'};[pscustomobject]@{Connection=$data;State='Solicitação concluída; conferir inventário'}|ConvertTo-Json -Compress";
    }
    public static string Server(string server) { NetworkDiagnostics.ValidateHost(server); return Prefix(server) + "ConvertTo-Json -InputObject @(Get-Printer -ComputerName $data|Where-Object Shared|Select-Object Name,ShareName,DriverName,Location) -Depth 4 -Compress"; }
    public static string Directory => Prefix() + """
        $root=[ADSI]'LDAP://RootDSE'
        $base=[ADSI]('LDAP://'+$root.defaultNamingContext)
        $query=New-Object System.DirectoryServices.DirectorySearcher($base)
        $query.Filter='(objectCategory=printQueue)';$query.PageSize=100;$query.SizeLimit=500;$query.ClientTimeout=[TimeSpan]::FromSeconds(15);$query.ServerTimeLimit=[TimeSpan]::FromSeconds(10)
        foreach($property in @('name','uNCName','location','driverName')){[void]$query.PropertiesToLoad.Add($property)}
        $rows=@();$results=$query.FindAll()
        try{foreach($r in $results){$rows+=[pscustomobject]@{Nome=[string]$r.Properties['name'][0];UNC=[string]$r.Properties['uncname'][0];Local=[string]$r.Properties['location'][0];Driver=[string]$r.Properties['drivername'][0]}}}finally{$results.Dispose();$query.Dispose()}
        ConvertTo-Json -InputObject $rows -Depth 4 -Compress
        """;
    public static string TestPage(string name) => Prefix(name) + "if([string]::IsNullOrWhiteSpace($data)){throw 'Selecione uma impressora'};$p=@(Get-CimInstance Win32_Printer|Where-Object {$_.Name -eq $data});if($p.Count -ne 1){throw 'Impressora não encontrada'};$r=Invoke-CimMethod -InputObject $p[0] -MethodName PrintTestPage;if($r.ReturnValue -ne 0){throw ('Spooler retornou '+$r.ReturnValue)};'Página enviada ao spooler; confirme a impressão física.'";
}
