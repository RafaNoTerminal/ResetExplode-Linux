namespace Diagnostico.Windows;
internal static class RicohSmoke
{
    internal static LocalPrinterPackage Fixture => new("Fixture Ricoh","DRIVERS/FixtureRicoh","fixture.inf",new string('a',64),"fixture.cat",new string('b',64),["Fixture Ricoh","Fixture Ricoh 2"],"Fixture Ricoh");
    internal static void Verify(){
        string mock="""
            $data=[pscustomobject]@{Inf='fixture.inf';Hash=('a'*64);Model='Fixture Ricoh'}
            $script:installed=$false;$script:stages=0;$script:adds=0;$script:code=0;$script:missing=$false;$script:throwRegistration=$false;$script:fallbackCode=0;$script:fallbackConfirm=$false;$script:fallbacks=0
            function Get-PrinterDriver { if($script:installed){[pscustomobject]@{Name='Fixture Ricoh';PrinterEnvironment='Windows x64'}} }
            function Get-Service {param($Name);[pscustomobject]@{Status='Running'}}
            function Get-FileHash {param($LiteralPath,$Algorithm);[pscustomobject]@{Hash=('a'*64)}}
            function Get-ChildItem {param($LiteralPath,[switch]$Directory,$Filter);[pscustomobject]@{FullName='C:\fixture-driverstore';Name='fixture.inf_amd64_fixture'}}
            function Test-Path {param($LiteralPath);$true}
            function Add-PrinterDriver {param($Name,$PrinterEnvironment,$InfPath);$script:adds++;if($script:throwRegistration){throw 'Erro CIM fixture 0x80070057'};if(-not $script:missing){$script:installed=$true}}
            $registerDriverFallback={param($inf,$model);$script:fallbacks++;if($script:fallbackConfirm){$script:installed=$true};[pscustomobject]@{Code=$script:fallbackCode;Text='PrintUI fixture only'}}
            $stageDriver={param($inf);$script:stages++;[pscustomobject]@{Code=$script:code;Text='fixture stage only'}}
            """;
        ProcessOutcome Execute(string script)=>Task.Run(()=>MainForm.UpdateProcess(script,CancellationToken.None)).GetAwaiter().GetResult();
        foreach(var state in new[]{"normal","existing","failed","reboot","unconfirmed","fallback-success","fallback-failed","fallback-reboot"}){
            string setup=state switch {"existing"=>"$script:installed=$true", "failed"=>"$script:code=5", "reboot"=>"$script:code=3010", "unconfirmed"=>"$script:missing=$true", "fallback-success"=>"$script:throwRegistration=$true;$script:fallbackConfirm=$true", "fallback-failed"=>"$script:throwRegistration=$true;$script:fallbackCode=5", "fallback-reboot"=>"$script:throwRegistration=$true;$script:fallbackCode=3010", _=>""};
            var outcome=Execute(mock+"\n"+setup+"\n$answer=& {\n"+LocalPrinterPackage.Body+"\n}\n"+"[pscustomobject]@{ResultJson=[string]$answer;Stages=$script:stages;Adds=$script:adds;Fallbacks=$script:fallbacks}|ConvertTo-Json -Depth 8 -Compress");
            if(outcome.ExitCode!=0)throw new Exception("Ricoh fixture "+state+": "+outcome.Error);
            using var data=System.Text.Json.JsonDocument.Parse(outcome.Output);var root=data.RootElement;var rows=OperationResults.Packages(root.GetProperty("ResultJson").GetString()!);int stages=root.GetProperty("Stages").GetInt32(),adds=root.GetProperty("Adds").GetInt32();
            if(state=="normal"&&(rows.Any(r=>r.Failed)||stages!=1||adds!=1||!RicohSetupService.Ready(new("fixture","",new(){["Etapas"]=rows},DateTimeOffset.Now))))throw new Exception("Driver Ricoh não confirmado na fixture: "+outcome.Output);
            if(state=="existing"&&(stages!=0||adds!=0||rows.Single().Estado!="Preservado"))throw new Exception("Driver existente foi reinstalado.");
            if(state is "failed" or "unconfirmed" && !rows.Any(r=>r.Failed))throw new Exception("Erro Ricoh foi ocultado.");
            if(state=="fallback-success"&&(rows.Any(r=>r.Failed)||!rows.Any(r=>r.Estado=="Alternativa")||root.GetProperty("Fallbacks").GetInt32()!=1))throw new Exception("PrintUI não recuperou a falha CIM ou diagnóstico sumiu: "+outcome.Output);
            if(state=="fallback-failed"&&!rows.Any(r=>r.Failed&&r.Detalhes.Contains("0x80070057")))throw new Exception("Falha CIM/PrintUI perdeu detalhes.");
            if(state=="fallback-reboot"&&!rows.Any(r=>r.Estado=="Pendente"))throw new Exception("PrintUI perdeu reboot pendente.");
            if(state=="reboot"&&(adds!=0||!rows.Any(r=>r.Estado=="Pendente")))throw new Exception("Continua driver após reboot pendente.");
        }
        string code=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Fixture.InstallScript("fixture.inf","Fixture Ricoh")));
        var parse=Execute("$t=$null;$e=$null;[System.Management.Automation.Language.Parser]::ParseInput([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+code+"')),[ref]$t,[ref]$e)|Out-Null;if($e.Count){throw ($e|Out-String)}");
        if(parse.ExitCode!=0)throw new Exception("Ricoh script sintaxe: "+parse.Error);
    }
}
