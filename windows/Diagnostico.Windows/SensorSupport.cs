using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
namespace Diagnostico.Windows;

internal static class SensorSupport
{
    internal const string Version = "2.2.0";
    internal const string Download = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    internal const string Hash = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
    internal static async Task<TestResult> Install(CancellationToken ct)
    {
        var directory = ActivationService.PrivateDirectory();
        try {
            string file = Path.Combine(directory.FullName, "PawnIO_setup.exe");
            using var client = SpeedTest.Client(); client.Timeout = TimeSpan.FromMinutes(2);
            using var response = await client.GetAsync(Download, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                var buffer = new byte[65536]; long count = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0) { count += read; if (count > 10 * 1048576) throw new IOException("Instalador excedeu o tamanho permitido."); await output.WriteAsync(buffer.AsMemory(0, read), ct); }
            }
            string digest; await using (var stream = File.OpenRead(file)) digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (digest != Hash) throw new IOException("SHA256 do instalador não corresponde ao release2.2.0 conferido. Nada foi executado.");
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(file));
            var signature = await MainForm.UpdateProcess("$ErrorActionPreference='Stop';$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "'));$s=Get-AuthenticodeSignature -LiteralPath $p;if($s.Status -ne 'Valid'){throw ('Assinatura do instalador não confirmada: '+$s.Status)};$s.SignerCertificate.Subject", ct);
            if (signature.ExitCode != 0) throw new IOException("Suporte de sensores não executado. " + signature.Error);
            ct.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }) ?? throw new IOException("Instalador não iniciou."); await process.WaitForExitAsync();
            return new("Suporte de sensores PawnIO", process.ExitCode is 0 or 3010 ? "Instalador concluiu; reabrir RBT e conferir sensores" : "Instalador não concluiu", new() { ["Versão"] = Version, ["Retorno"] = process.ExitCode, ["SHA256"] = Hash, ["Assinante"] = signature.Output, ["Observação"] = "Driver opcional instalado pelo instalador original. Feche e reabra o RBT para recarregar a biblioteca. Se o instalador indicar reinício, faça-o manualmente. Nenhuma proteção de drivers foi desativada." }, DateTimeOffset.Now);
        } finally { try { directory.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
