namespace Diagnostico.Windows;
internal static class PrinterRemovalSmoke
{
    internal static void Verify(){
        string mock="""
            $script:queues=[Collections.Generic.List[object]]::new()
            $script:queues.Add([pscustomobject]@{Name='Fixture [literal]';DriverName='Fixture driver';PortName='port1';Shared=$false;ShareName=''})
            $script:queues.Add([pscustomobject]@{Name='Preserve';DriverName='Fixture driver';PortName='port2';Shared=$false;ShareName=''})
            $script:jobs=$false;$script:removes=0;$script:default=$false
            function Get-Printer {$script:queues.ToArray()}
            function Get-CimInstance {param($ClassName);foreach($q in $script:queues){[pscustomobject]@{Name=$q.Name;Default=($script:default -and $q.Name -eq 'Fixture [literal]')}}}
            function Get-PrintJob {param($PrinterName);if($script:jobs){[pscustomobject]@{ID=1}}}
            function Get-PrintConfiguration {param($PrinterName);[pscustomobject]@{PrintTicketXML='<fixture />'}}
            function Remove-Printer {param($InputObject,[switch]$Confirm);$script:removes++;[void]$script:queues.Remove($InputObject)}
            function Remove-PrinterPort {throw 'Ports must be preserved'}
            function Remove-PrinterDriver {throw 'Drivers must be preserved'}
            """;
        string file=Path.Combine(Path.GetTempPath(),"rbt-printer-removal-fixture-"+Guid.NewGuid().ToString("N")+".json");
        try{
            foreach(var state in new[]{"normal","jobs","changed","absent","default-blocked","default-authorized"}){
                var chosen=new InstalledPrinterChoice(state=="absent"?"Missing fixture":"Fixture [literal]","Fixture driver",state=="changed"?"different-port":"port1",state=="default-authorized");
                string script=PrinterRemoval.Build([chosen],file,state=="default-authorized");int position=script.IndexOf("$rows=");
                script=script[..position]+"\n"+mock+"\n"+(state=="jobs"?"$script:jobs=$true\n":state.StartsWith("default-")?"$script:default=$true\n":"")+"$answer=& {\n"+script[position..]+"\n}\n[pscustomobject]@{Json=[string]$answer;Remaining=@($script:queues.ToArray()|Select-Object -ExpandProperty Name);Removes=$script:removes}|ConvertTo-Json -Depth 8 -Compress";
                var outcome=Task.Run(()=>MainForm.UpdateProcess(script,CancellationToken.None)).GetAwaiter().GetResult();
                if(outcome.ExitCode!=0)throw new Exception("Printer removal fixture: "+outcome.Error);
                using var data=System.Text.Json.JsonDocument.Parse(outcome.Output);var rows=OperationResults.Packages(data.RootElement.GetProperty("Json").GetString()!);int removes=data.RootElement.GetProperty("Removes").GetInt32();
                if(!File.Exists(file))throw new Exception("Printer removal did not save backup first.");
                if(state is "normal" or "default-authorized" &&(removes!=1||rows.Single().Estado!="Removido"||!data.RootElement.GetProperty("Remaining").EnumerateArray().Any(x=>x.GetString()=="Preserve")))throw new Exception("Exclusão não ficou limitada à fila selecionada: "+outcome.Output);
                if(state is "jobs" or "changed" or "default-blocked" &&(removes!=0||!rows.Any(r=>r.Failed)))throw new Exception("Removeu fila com jobs ou identidade alterada.");
                if(state=="absent"&&(removes!=0||rows.Single().Estado!="Ausente"))throw new Exception("Fila ausente executou exclusão.");
            }
        }finally{if(File.Exists(file))File.Delete(file);}
    }
}
