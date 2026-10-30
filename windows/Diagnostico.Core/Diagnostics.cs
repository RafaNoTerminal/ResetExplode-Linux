using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Diagnostico;

public static class Brand
{
    public const string Name = "RBT Diagnóstico";
    public const string Version = "0.5.2";
}

public record TestResult(string Teste, string Estado, Dictionary<string, object?> Dados, DateTimeOffset Data)
{
    public static TestResult Done(string name, params (string, object?)[] fields) =>
        new(name, "Concluído", fields.ToDictionary(x => x.Item1, x => x.Item2), DateTimeOffset.Now);
}

public static class Benchmarks
{
    public static async Task<TestResult> Cpu(int workers, TimeSpan duration, CancellationToken ct)
    {
        if (workers < 1 || workers > Environment.ProcessorCount || duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(workers));
        using var start = new ManualResetEventSlim(false);
        var watch = new Stopwatch();
        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(() => {
            var data = new byte[1024 * 1024]; RandomNumberGenerator.Fill(data);
            Span<byte> hash = stackalloc byte[32]; long bytes = 0;
            start.Wait(ct);
            while (watch.Elapsed < duration) { ct.ThrowIfCancellationRequested(); SHA256.HashData(data, hash); bytes += data.Length; }
            return bytes;
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        watch.Start(); start.Set();
        long total = (await Task.WhenAll(tasks)).Sum();
        return TestResult.Done("CPU · SHA-256", ("Threads", workers), ("Duração (s)", Math.Round(watch.Elapsed.TotalSeconds, 2)),
            ("Processamento (MiB/s)", Math.Round(total / watch.Elapsed.TotalSeconds / 1048576, 2)),
            ("Método", "SHA-256; compare somente o mesmo método, versão e número de threads."));
    }

    public static Task<TestResult> Memory(int mib, TimeSpan duration, CancellationToken ct) => Task.Run(() => {
        if (mib is < 1 or > 256 || duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(mib));
        var data = new byte[mib * 1048576]; long passes = 0; var watch = Stopwatch.StartNew();
        do {
            byte pattern = (byte)((passes % 2 == 0) ? 0xA5 : 0x5A);
            for (int block = 0; block < data.Length; block += 65536) {
                ct.ThrowIfCancellationRequested(); Array.Fill(data, pattern, block, Math.Min(65536, data.Length - block));
            }
            for (int block = 0; block < data.Length; block += 65536) {
                ct.ThrowIfCancellationRequested();
                for (int i = block; i < Math.Min(block + 65536, data.Length); i++)
                    if (data[i] != pattern) throw new InvalidDataException("Falha na verificação da memória reservada.");
            }
            passes++;
        } while (watch.Elapsed < duration);
        return TestResult.Done("Memória · padrões alternados", ("Memória verificada (MiB)", mib), ("Passagens", passes),
            ("Duração (s)", Math.Round(watch.Elapsed.TotalSeconds, 2)), ("Resultado", "Sem divergência na região reservada."),
            ("Limite", "Não substitui um teste de toda a RAM iniciado fora do Windows."));
    }, ct);

    public static async Task<TestResult> Disk(string directory, int mib, CancellationToken ct)
    {
        if (mib is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(mib));
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!);
        if (drive.AvailableFreeSpace < (mib + 128L) * 1048576) throw new IOException("Espaço livre insuficiente para o teste temporário.");
        string path = Path.Combine(directory, "rbt-diag-" + Guid.NewGuid().ToString("N") + ".tmp");
        byte[] data = new byte[1048576]; RandomNumberGenerator.Fill(data);
        byte[] expected = SHA256.HashData(data); var watch = Stopwatch.StartNew();
        bool created = false;
        try {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1048576,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            created = true;
            for (int i = 0; i < mib; i++) await file.WriteAsync(data, ct);
            ct.ThrowIfCancellationRequested(); file.Flush(true);
            double write = mib / watch.Elapsed.TotalSeconds; file.Position = 0; watch.Restart();
            for (int i = 0; i < mib; i++) {
                await file.ReadExactlyAsync(data, ct);
                if (!CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(data))) throw new InvalidDataException("O arquivo lido diverge dos dados gravados.");
            }
            return TestResult.Done("Disco · arquivo temporário", ("Pasta testada", directory), ("Tamanho (MiB)", mib),
                ("Gravação com flush (MiB/s)", Math.Round(write, 2)), ("Leitura e verificação (MiB/s)", Math.Round(mib / watch.Elapsed.TotalSeconds, 2)),
                ("Integridade", "SHA-256 conferido em todos os blocos."), ("Método", "E/S sequencial com cache do sistema; leitura inclui cálculo SHA-256. Não é I/O direto nem teste completo do disco."));
        } finally { if (created && File.Exists(path)) File.Delete(path); }
    }
}

public static class NetworkDiagnostics
{
    public static string ValidateHost(string input)
    {
        string host = input.Trim();
        if (host.Length is < 1 or > 253 || host.Any(char.IsWhiteSpace) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException("Informe apenas um nome DNS ou endereço IP, sem URL, porta ou espaços.");
        return host;
    }

    public static async Task<TestResult> PingHost(string input, CancellationToken ct, int count = 10)
    {
        string host = ValidateHost(input); var samples = new List<double>(); int lost = 0;
        if (count is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(count));
        using var dnsTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); dnsTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        IPAddress[] addresses;
        try { addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, dnsTimeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("DNS não respondeu em 5 segundos. Confira DNS, VPN e conexão da empresa."); }
        addresses = addresses.OrderBy(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1).ToArray();
        if (addresses.Length == 0) throw new IOException("O nome não foi resolvido.");
        using var ping = new Ping();
        for (int i = 0; i < count; i++) {
            ct.ThrowIfCancellationRequested();
            try {
                var r = await ping.SendPingAsync(addresses[0], TimeSpan.FromSeconds(2), cancellationToken: ct);
                if (r.Status == IPStatus.Success) samples.Add(r.RoundtripTime); else lost++;
            } catch (PingException) { lost++; }
            if (i + 1 < count) await Task.Delay(200, ct);
        }
        return TestResult.Done("Rede · DNS e ping", ("Destino", host), ("IP resolvido", addresses[0].ToString()),
            ("Respostas", samples.Count), ("Perda ICMP (%)", lost * 100.0 / count),
            ("Latência média (ms)", samples.Count > 0 ? Math.Round(samples.Average(), 2) : null),
            ("Variação média entre respostas (ms)", samples.Count > 1 ? Math.Round(samples.Skip(1).Zip(samples, (a, b) => Math.Abs(a - b)).Average(), 2) : null),
            ("Observação", "Ausência de resposta também pode ser bloqueio de ICMP no destino ou na rede."));
    }
    public static async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(timeout);
        try { return await operation(deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("O diagnóstico atingiu o limite de tempo. Confira DNS, proxy, firewall e acesso ao destino. Isso não foi cancelamento pelo usuário."); }
    }
    public static async Task<TestResult> Tcp(string input, int port, CancellationToken ct)
    {
        string host = ValidateHost(input); if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var watch = Stopwatch.StartNew(); using var client = new System.Net.Sockets.TcpClient();
        try {
            await WithTimeout(async token => { await client.ConnectAsync(host, port, token); return true; }, TimeSpan.FromSeconds(5), ct);
            return TestResult.Done("Conexão TCP", ("Destino", host), ("Porta", port), ("Conectou", true), ("Tempo ms", watch.ElapsedMilliseconds), ("Limite", "Porta acessível não comprova login, permissão ou funcionamento da aplicação/impressora."));
        } catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException) {
            return new("Conexão TCP", "Não conectou", new() { ["Destino"] = host, ["Porta"] = port, ["Erro"] = e.Message }, DateTimeOffset.Now);
        }
    }
    public static async Task<TestResult> Internet(CancellationToken ct)
    {
        using var client = SpeedTest.Client(); var watch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Head, "https://www.microsoft.com/");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        return TestResult.Done("Conectividade HTTPS", ("Destino", "https://www.microsoft.com/"), ("HTTP", (int)response.StatusCode), ("Tempo ms", watch.ElapsedMilliseconds), ("Observação", response.IsSuccessStatusCode ? "Servidor HTTPS respondeu. Não é medição de velocidade." : "Servidor/proxy respondeu; código HTTP pode indicar bloqueio ou restrição. Não é aprovação de acesso a todos os serviços."));
    }

}

public static class Reports
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string Json(object data) => JsonSerializer.Serialize(data, JsonOptions);
    public static string Html(object data)
    {
        string Render(JsonElement e) => e.ValueKind switch {
            JsonValueKind.Object => "<table>" + string.Concat(e.EnumerateObject().Select(p => "<tr><th>" + WebUtility.HtmlEncode(p.Name) + "</th><td>" + Render(p.Value) + "</td></tr>")) + "</table>",
            JsonValueKind.Array => "<ul>" + string.Concat(e.EnumerateArray().Select(v => "<li>" + Render(v) + "</li>")) + "</ul>",
            _ => WebUtility.HtmlEncode(e.ValueKind == JsonValueKind.Null ? "Indisponível" : e.ToString())
        };
        return "<!doctype html><html lang=\"pt-BR\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>" + Brand.Name + "</title><style>body{font:15px system-ui;max-width:1100px;margin:32px auto;padding:16px;color:#242830}table{border-collapse:collapse;width:100%;margin:12px 0}th,td{border:1px solid #ccd1d8;padding:9px;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{background:#f0f3f6}h1{color:#176078}@media print{body{margin:0}tr{break-inside:avoid}}</style><h1>" + Brand.Name + " · Windows</h1><p>Relatório local de diagnóstico. Resultados representam as condições e amostras descritas.</p>" + Render(JsonSerializer.SerializeToElement(data)) + "</html>";
    }
}
