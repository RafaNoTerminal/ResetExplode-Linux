using System.Net;
using System.Net.Sockets;
namespace Diagnostico.Windows;

internal static class CompanySmoke
{
    internal static void Verify()
    {
        var fixture = new CompanyPrinter("TST", "Existing fixture", "192.0.2.20");
        var choices = new[] { fixture, new CompanyPrinter("NEW", "New fixture", "192.0.2.21") };
        string mock = """
            $script:printers=[Collections.Generic.List[object]]::new()
            $script:printers.Add([pscustomobject]@{Name='Existing fixture';PortName='oldport';DriverName='Old driver'})
            $script:ports=[Collections.Generic.List[object]]::new()
            $script:ports.Add([pscustomobject]@{Name='oldport';PrinterHostAddress='192.0.2.20'})
            function Get-CimInstance { param($ClassName); @() }
            function Get-PrinterDriver { [pscustomobject]@{Name='Fixture driver'} }
            function Get-Service { param($Name);[pscustomobject]@{Status='Running'} }
            function Get-Printer { $script:printers.ToArray() }
            function Get-PrinterPort { $script:ports.ToArray() }
            function Add-PrinterPort { param($Name,$PrinterHostAddress,$PortNumber);$script:ports.Add([pscustomobject]@{Name=$Name;PrinterHostAddress=$PrinterHostAddress}) }
            function Add-Printer { param($Name,$DriverName,$PortName);$script:printers.Add([pscustomobject]@{Name=$Name;DriverName=$DriverName;PortName=$PortName}) }
            function Remove-Printer { throw 'Removal forbidden in fixture' }
            function Remove-PrinterPort { throw 'Removal forbidden in fixture' }
            """;
        var output = Task.Run(() => MainForm.UpdateProcess(mock + "\n" + PrinterScripts.Install(choices, "Fixture driver"), CancellationToken.None)).GetAwaiter().GetResult();
        if (output.ExitCode != 0) throw new Exception("Printer fixture: " + output.Error);
        var rows = OperationResults.Packages(output.Output);
        if (rows.Count != 2 || rows[0].Estado != "Preservado" || rows[1].Estado != "Aplicado") throw new Exception("Filas existentes não foram preservadas ou nova fila não foi conferida: " + output.Output);
        var ping = Task.Run(() => NetworkDiagnostics.PingHost("127.0.0.1", CancellationToken.None, 1)).GetAwaiter().GetResult();
        if (Convert.ToInt32(ping.Dados["Respostas"]) != 1) throw new Exception("Ping local real falhou.");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var tcp = Task.Run(() => NetworkDiagnostics.Tcp("127.0.0.1", port, CancellationToken.None)).GetAwaiter().GetResult();
        if (!tcp.Dados.TryGetValue("Conectou", out var value) || value is not true) throw new Exception("TCP local real falhou.");
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "rbt-cleanup-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        try {
            string old = Path.Combine(path, "old.tmp"), recent = Path.Combine(path, "recent.tmp"); File.WriteAllText(old, "old"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10)); File.WriteAllText(recent, "preserve");
            var preview = TempCleanup.Preview([path]); if (preview.Count != 1 || preview[0].Path != old) throw new Exception("Prévia de limpeza incluiu arquivo recente.");
            _ = TempCleanup.Apply(preview.ToArray());
            if (File.Exists(old) || !File.Exists(recent)) throw new Exception("Limpeza controlada falhou/preservação de recente falhou.");
        } finally { Directory.Delete(path, true); }
    }
}
