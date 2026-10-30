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
        if (printers.Any(p => p.Receipt) && (printers.Any(p => !p.Receipt) || !System.Text.RegularExpressions.Regex.IsMatch(driver, @"(?i)elgin.*i.?9|i.?9.*elgin"))) throw new ArgumentException("Instale comprovantes separadamente e selecione o driver Elgin i9 correto.");
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
    public static string ConfigureReceipts(CompanyPrinter[] printers, string backupDirectory) {
        if (printers.Length == 0 || printers.Any(p => !p.Receipt)) throw new ArgumentException("Selecione somente filas de comprovantes.");
        new CompanyProfile(@"\\fixture\share", printers, []).Validate();
        return Prefix(new { Printers = printers, BackupDirectory = backupDirectory }) + ReceiptBody;
    }
    public static string RestoreReceipt(string file) => Prefix(new { RestoreFile = file }) + ReceiptBody;
    const string ReceiptBody = """
        $rows=[Collections.Generic.List[object]]::new()
        $restore=[bool]$data.RestoreFile
        if($restore){
          $saved=Get-Content -LiteralPath $data.RestoreFile -Raw|ConvertFrom-Json
          if($saved.Computer -ne $env:COMPUTERNAME){throw 'Backup de outro computador.'}
          $targets=@([pscustomobject]@{Name=$saved.Name;Ip=$saved.Ip})
        }else{$targets=@($data.Printers)}
        foreach($p in $targets){try{
          $queue=@(Get-Printer|Where-Object {$_.Name -eq $p.Name})
          if($queue.Count -ne 1){throw 'Instale a fila antes de ajustar as preferências.'}
          $queue=$queue[0]
          if($queue.DriverName -notmatch '(?i)elgin.*i.?9|i.?9.*elgin'){throw 'A fila não usa driver Elgin i9; nenhum ajuste aplicado.'}
          $port=Get-PrinterPort|Where-Object {$_.Name -eq $queue.PortName}
          if($port.PrinterHostAddress -ne $p.Ip){throw 'Destino da fila diverge do catálogo/backup; ajustes preservados.'}
          if(@(Get-PrintJob -PrinterName $p.Name -ErrorAction Stop).Count){throw 'Há trabalhos pendentes; aguarde antes de ajustar.'}
          $cfg=Get-PrintConfiguration -PrinterName $p.Name -ErrorAction Stop
          if($restore){
            if($queue.DriverName -ne $saved.Driver -or $queue.PortName -ne $saved.Port){throw 'Driver/porta mudou desde o backup; revise manualmente.'}
            Set-PrintConfiguration -PrinterName $p.Name -PrintTicketXml $saved.PrintTicketXml
            Set-Printer -Name $p.Name -Comment $saved.Comment
            $targetXml=[xml]$saved.PrintTicketXml
          }else{
            New-Item -ItemType Directory -Force -Path $data.BackupDirectory|Out-Null
            $file=Join-Path $data.BackupDirectory ('comprovantes-'+[guid]::NewGuid().ToString('N')+'.json')
            [pscustomobject]@{Computer=$env:COMPUTERNAME;Name=$p.Name;Ip=$p.Ip;Driver=$queue.DriverName;Port=$queue.PortName;Comment=$queue.Comment;PrintTicketXml=$cfg.PrintTicketXML}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $file -Encoding UTF8
            $rows.Add([pscustomobject]@{Acao='Backup comprovantes';Pacote=$p.Name;Escopo='Fila selecionada';Estado='Aplicado';Detalhes=$file})
            [xml]$ticket=$cfg.PrintTicketXML
            if(-not $ticket.DocumentElement){throw 'Driver não expôs PrintTicket; configure em Preferências da fila.'}
            $ns='http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework'
            $keys='http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords'
            $ticket.DocumentElement.SetAttribute('xmlns:psk',$keys)
            foreach($old in @($ticket.SelectNodes("//*[local-name()='Feature']")|Where-Object {$_.GetAttribute('name').Split(':')[-1] -eq 'PageMediaSize'})){[void]$old.ParentNode.RemoveChild($old)}
            $feature=$ticket.CreateElement('psf','Feature',$ns);$feature.SetAttribute('name','psk:PageMediaSize')
            $option=$ticket.CreateElement('psf','Option',$ns);$option.SetAttribute('name','psk:CustomMediaSize');[void]$feature.AppendChild($option)
            foreach($size in @(@('MediaSizeWidth','72000'),@('MediaSizeHeight','100000'))){
              $property=$ticket.CreateElement('psf','ScoredProperty',$ns);$property.SetAttribute('name',('psk:'+$size[0]))
              $value=$ticket.CreateElement('psf','Value',$ns);$value.InnerText=$size[1];[void]$property.AppendChild($value);[void]$option.AppendChild($property)
            }
            [void]$ticket.DocumentElement.AppendChild($feature)
            Set-PrintConfiguration -PrinterName $p.Name -PrintTicketXml $ticket.OuterXml
            Set-PrintConfiguration -PrinterName $p.Name -Color $false -DuplexingMode OneSided
            Set-Printer -Name $p.Name -Comment 'Elgin i9 - comprovantes - 72x100 mm; conferir corte no driver'
            $targetXml=$ticket
          }
          $after=Get-PrintConfiguration -PrinterName $p.Name -ErrorAction Stop
          if(-not $restore){
            [xml]$verified=$after.PrintTicketXML
            function Read-Dimension($dimension){$v=$verified.SelectNodes("//*[local-name()='Feature']")|Where-Object {$_.GetAttribute('name').Split(':')[-1] -eq 'PageMediaSize'}|ForEach-Object {$_.SelectNodes(".//*[local-name()='ScoredProperty']")}|Where-Object {$_.GetAttribute('name').Split(':')[-1] -eq $dimension}|ForEach-Object {$_.SelectSingleNode("*[local-name()='Value']").InnerText};return [string]($v|Select-Object -First 1)}
            if((Read-Dimension 'MediaSizeWidth') -ne '72000' -or (Read-Dimension 'MediaSizeHeight') -ne '100000' -or $after.Color -ne $false -or [string]$after.DuplexingMode -ne 'OneSided'){throw ('O driver não confirmou 72x100 mm/monocromático/frente única. Configure nas Preferências ou restaure o backup '+$file)}
          }elseif(([xml]$after.PrintTicketXML).OuterXml -ne $targetXml.OuterXml){throw 'Driver normalizou ou não confirmou o ticket restaurado; confira as preferências.'}
          $rows.Add([pscustomobject]@{Acao='Preferências comprovantes';Pacote=$p.Name;Escopo='Fila selecionada';Estado='Aplicado';Detalhes=$(if($restore){'Ticket anterior restaurado e conferido.'}else{'72x100 mm, monocromático e frente única relidos no driver. Fila/porta/padrão preservados.'})})
          if(-not $restore){$rows.Add([pscustomobject]@{Acao='Corte parcial';Pacote=$p.Name;Escopo='Driver Elgin';Estado='Pendente';Detalhes='Perfil .dat não fornecido. Configure corte parcial nas preferências Elgin e teste um comprovante real.'})}
        }catch{$rows.Add([pscustomobject]@{Acao='Preferências comprovantes';Pacote=$p.Name;Escopo='Fila selecionada';Estado='Erro';Detalhes=$_.Exception.Message})}}
        ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 5 -Compress
        """;
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
