using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace Diagnostico.Windows;

// A fresh helper reloads PawnIO after installation without losing the user's app session/results.
internal sealed class SensorMonitor : IDisposable
{
    readonly object gate = new(); Process? worker;
    internal bool Active => worker is { HasExited: false };
    internal string Notice { get; private set; } = "Leitura não iniciada. Clique em Ler / recarregar sensores.";
    internal List<SensorRow> Read(bool start = false, bool fixture = false)
    {
        lock (gate) {
            if (start && !Active) {
                var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
                if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(typeof(Program).Assembly.Location);
                info.ArgumentList.Add(fixture ? "--sensor-worker-fixture" : "--sensor-worker");
                worker = Process.Start(info) ?? throw new IOException("Não foi possível iniciar o leitor de sensores.");
            }
            if (!Active) return [];
            worker!.StandardInput.WriteLine("read"); worker.StandardInput.Flush();
            string? line = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            if (line is null) throw new IOException("O leitor de sensores encerrou sem amostra. " + worker.StandardError.ReadToEnd());
            var snapshot = JsonSerializer.Deserialize<SensorSnapshot>(line) ?? throw new IOException("Amostra inválida do leitor.");
            Notice = snapshot.Notice; return snapshot.Rows;
        }
    }
    public void Dispose()
    {
        lock (gate) {
            if (worker is not null) {
                try { if (!worker.HasExited) { worker.StandardInput.WriteLine("close"); worker.StandardInput.Flush(); if (!worker.WaitForExit(3000)) worker.Kill(true); } }
                catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                worker.Dispose(); worker = null;
            }
            Notice = "Leitura interrompida; última amostra preservada.";
        }
    }
}
