using System.Collections.Concurrent;
namespace Diagnostico.Windows;

internal static class RegressionSmoke
{
    internal static void Verify() {
        var chunks = new ConcurrentQueue<string>();
        string shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        string command = "[Console]::Out.Write('10%'+[char]13);[Console]::Out.Flush();Start-Sleep -Milliseconds 800;[Console]::Out.Write('100%');[Console]::Out.Flush()";
        var execute = Task.Run(() => MaintenanceRunner.Execute(MaintenanceCatalog.Get("dism-check"), chunks.Enqueue, shell, ["-NoProfile", "-NonInteractive", "-Command", command]));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (chunks.IsEmpty && !execute.IsCompleted && watch.ElapsedMilliseconds < 10000) Thread.Sleep(10);
        if (chunks.IsEmpty || execute.IsCompleted) throw new Exception("Progresso sem newline não chegou antes da conclusão.");
        var result = execute.GetAwaiter().GetResult();
        if (result.Code != 0 || !result.Output.Contains("10%\r100%")) throw new Exception("Saída incremental perdeu progresso: " + result.Output + result.Error);
        string directory = Path.Combine(Path.GetTempPath(), "rbt-receipt-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try {
            string mock = """
                $script:queue=[pscustomobject]@{Name='Receipt fixture';DriverName='ELGIN i9';PortName='port';Comment='original'}
                $script:cfg=[pscustomobject]@{Color=$true;DuplexingMode='TwoSidedLongEdge';PrintTicketXML='<psf:PrintTicket xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework" xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords" version="1" />'}
                function Get-Printer {$script:queue}
                function Get-PrinterPort {[pscustomobject]@{Name='port';PrinterHostAddress='192.0.2.22'}}
                function Get-PrintJob {param($PrinterName);@()}
                function Get-PrintConfiguration {param($PrinterName);$script:cfg}
                function Set-PrintConfiguration {param($PrinterName,$PrintTicketXml,[bool]$Color,$DuplexingMode)
                  if($PrintTicketXml -and -not $script:reject){$script:cfg.PrintTicketXML=$PrintTicketXml}
                  if($PSBoundParameters.ContainsKey('Color')){$script:cfg.Color=$Color}
                  if($DuplexingMode){$script:cfg.DuplexingMode=$DuplexingMode}
                }
                function Set-Printer {param($Name,$Comment);$script:queue.Comment=$Comment}
                """;
            var printer = new CompanyPrinter("FIX", "Receipt fixture", "192.0.2.22", Receipt: true);
            var output = Task.Run(() => MainForm.UpdateProcess(mock + "\n" + PrinterScripts.ConfigureReceipts([printer], directory), CancellationToken.None)).GetAwaiter().GetResult();
            var rows = OperationResults.Packages(output.Output);
            if (output.ExitCode != 0 || rows.Any(r => r.Failed) || !rows.Any(r => r.Estado == "Pendente")) throw new Exception("Comprovante fixture perdeu ajuste ou pendência do corte: " + output.Output + output.Error);
            if (Directory.GetFiles(directory, "comprovantes-*.json").Length != 1) throw new Exception("Backup de comprovante não foi criado antes de alterar.");
            output = Task.Run(() => MainForm.UpdateProcess(mock + "\n$script:reject=$true\n" + PrinterScripts.ConfigureReceipts([printer], directory), CancellationToken.None)).GetAwaiter().GetResult();
            if (output.ExitCode != 0 || !OperationResults.Packages(output.Output).Any(r => r.Failed)) throw new Exception("Driver que recusa papel foi marcado aplicado.");
            // Syntax only: never create a shortcut or change a production printer in CI.
            foreach (string script in new[] { PrinterScripts.RestoreReceipt("fixture"), OnboardingService.ChromeShortcut(new() { Title = "Fixture", Kind = OnboardingKind.ChromeApp, Url = "https://example.com/" }) }) {
                string data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(script));
                output = Task.Run(() => MainForm.UpdateProcess("$t=$null;$e=$null;[System.Management.Automation.Language.Parser]::ParseInput([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + data + "')),[ref]$t,[ref]$e)|Out-Null;if($e.Count){throw ($e|Out-String)}", CancellationToken.None)).GetAwaiter().GetResult();
                if (output.ExitCode != 0) throw new Exception("Sintaxe preferências/atalho: " + output.Error);
            }
        } finally { Directory.Delete(directory, true); }
    }
}
public sealed partial class MainForm
{
    internal void Verify050Ui() {
        menu.SelectedIndex = 12; Application.DoEvents();
        var previous = sensorRows;
        sensorRows = Enumerable.Range(0, 100).Select(i => new SensorRow("Fixture", "Temperature", "Sensor " + i.ToString("D3"), "35°", "30°", "40°", "°C", "fixture/" + i)).ToList(); RenderSensorRows(); Application.DoEvents();
        sensorGrid!.CurrentCell = sensorGrid.Rows[40].Cells[0]; sensorGrid.FirstDisplayedScrollingRowIndex = 35;
        var binding = sensorGrid.DataSource;
        for (int j = 0; j < 3; j++) { sensorRows = sensorRows.Select(r => r with { Atual = (36 + j) + "°" }).ToList(); RenderSensorRows(); Application.DoEvents(); }
        if (!ReferenceEquals(binding, sensorGrid.DataSource) || sensorGrid.FirstDisplayedScrollingRowIndex != 35 || sensorGrid.CurrentRow?.Index != 40) throw new Exception("Atualização de temperatura perdeu rolagem/seleção.");
        sensorRows = previous; RenderSensorRows();
        menu.SelectedIndex = 14; Application.DoEvents();
        if (maintenanceView?.TextLength == 0 || maintenanceStatus?.Text.Length == 0) throw new Exception("Manutenção abriu vazia.");
        menu.SelectedIndex = 15; Application.DoEvents();
        if (content.Controls.OfType<CheckedListBox>().Single().Items.Count != 10 || content.Controls.OfType<FlowLayoutPanel>().SelectMany(p => p.Controls.OfType<Button>()).Any(b => b.Text.Contains("Conectar com conta"))) throw new Exception("Central não foi convertida para preparação por setor.");
        menu.SelectedIndex = 0;
    }
}
