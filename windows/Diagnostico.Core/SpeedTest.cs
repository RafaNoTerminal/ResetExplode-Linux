using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Diagnostico;

public record SpeedServer(string Name, Uri Download, Uri Upload, Uri Latency, long Size);
public record SpeedProgress(string Stage, double Mbps, long Bytes);

public static class SpeedTest
{
    const long Mib = 1048576;
    public static HttpClient Client() {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(4), MaxConnectionsPerServer = 16 };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RBT-Diagnostico/0.2.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "identity");
        client.DefaultRequestHeaders.CacheControl = new() { NoCache = true };
        return client;
    }
    public static bool ValidEndpoint(Uri uri) => uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrWhiteSpace(uri.Host);
    public static async Task<double> Latency(HttpClient client, Uri uri, CancellationToken ct) {
        var watch = Stopwatch.StartNew();
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        string text = await SmallResponse(stream, ct);
        if (!text.Contains("test=test", StringComparison.Ordinal)) throw new IOException("Resposta de latência inválida.");
        return watch.Elapsed.TotalMilliseconds;
    }
    static async Task<string> SmallResponse(Stream stream, CancellationToken ct) {
        byte[] data = new byte[1025]; int offset = 0, n;
        while (offset < data.Length && (n = await stream.ReadAsync(data.AsMemory(offset), ct)) > 0) offset += n;
        if (offset > 1024) throw new IOException("Resposta do servidor excedeu o limite.");
        return System.Text.Encoding.UTF8.GetString(data, 0, offset);
    }
    public static async Task<SpeedServer> Discover(CancellationToken ct, Func<HttpClient>? createClient = null) {
        createClient ??= Client;
        using var directory = createClient();
        using var response = await directory.GetAsync("https://www.speedtest.net/api/js/servers?engine=js&limit=12&https_functional=true", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var block = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(block, ct)) > 0) { if (buffer.Length + read > 262144) throw new IOException("Diretório excedeu o limite."); buffer.Write(block, 0, read); }
        using var json = JsonDocument.Parse(buffer.ToArray());
        var servers = json.RootElement.EnumerateArray().Take(12).Select(x => x.Clone()).ToArray();
        var probes = servers.Select(async item => {
            try {
                using var client = createClient();
                if (!Uri.TryCreate(item.GetProperty("url").GetString(), UriKind.Absolute, out var source) || source.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(source.UserInfo)) return ((SpeedServer?, double)?)(null);
                var download = new Uri(source, "random4000x4000.jpg");
                using var head = new HttpRequestMessage(HttpMethod.Head, download);
                using var resp = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct); resp.EnsureSuccessStatusCode();
                var target = resp.RequestMessage?.RequestUri; long size = resp.Content.Headers.ContentLength ?? 0;
                if (target is null || !ValidEndpoint(target) || size < 65536 || size > 64 * Mib) return null;
                string Field(string key) => item.TryGetProperty(key, out var v) ? v.ToString() : "";
                var server = new SpeedServer(string.Join(" · ", new[] { Field("sponsor"), Field("name"), Field("country") }.Where(x => x.Length > 0)), target, new Uri(target, "upload.php"), new Uri(target, "latency.txt"), size);
                await Latency(client, server.Latency, ct);
                double a = await Latency(client, server.Latency, ct), b = await Latency(client, server.Latency, ct);
                return ((SpeedServer?, double)?)(server, (a + b) / 2);
            } catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException or KeyNotFoundException) { return null; }
        });
        var reachable = (await Task.WhenAll(probes)).Where(x => x.HasValue).OrderBy(x => x!.Value.Item2).ToArray();
        ct.ThrowIfCancellationRequested();
        return reachable.FirstOrDefault()?.Item1 ?? throw new IOException("Nenhum servidor de medição com TLS válido respondeu. Verifique internet, proxy ou firewall.");
    }

    public static async Task<TestResult> Run(IProgress<SpeedProgress>? progress, CancellationToken ct, SpeedServer? server = null, int seconds = 8, long budget = 1024 * Mib, int connections = 8, Func<HttpClient>? createClient = null) {
        if (seconds is < 1 or > 15 || budget < Mib || budget > 1024 * Mib || connections is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(seconds));
        progress?.Report(new("Selecionando servidor próximo…", 0, 0));
        createClient ??= Client;
        server ??= await Discover(ct, createClient);
        if (!ValidEndpoint(server.Download) || !ValidEndpoint(server.Upload) || !ValidEndpoint(server.Latency) || server.Size < 65536 || server.Size > 64 * Mib) throw new ArgumentException("Servidor requer HTTPS válido e tamanho de download confirmado.");
        using var client = createClient();
        await Latency(client, server.Latency, ct);
        var latencies = new List<double>();
        for (int i = 0; i < 5; i++) latencies.Add(await Latency(client, server.Latency, ct));
        var download = await Transfer(server, false, progress, ct, seconds, budget, connections, createClient);
        double? upload = null; string? warning = null;
        try { upload = await Transfer(server, true, progress, ct, seconds, budget, connections, createClient); }
        catch (Exception e) when (e is IOException or HttpRequestException) { warning = "Upload indisponível: " + e.Message; }
        return TestResult.Done("Velocidade da internet", ("Servidor", server.Name), ("Download (Mbps)", download), ("Upload (Mbps)", upload),
            ("Latência HTTP média (ms)", Math.Round(latencies.Average(), 2)),
            ("Variação HTTP (ms)", Math.Round(latencies.Skip(1).Zip(latencies, (a, b) => Math.Abs(a - b)).Average(), 2)),
            ("Aviso", warning), ("Método", "Motor próprio, até 8 conexões HTTPS. Não utiliza CLI externo; servidores públicos de terceiros. Não mede bufferbloat ou perda de pacotes."));
    }

    static async Task<double> Transfer(SpeedServer server, bool upload, IProgress<SpeedProgress>? progress, CancellationToken ct, int seconds, long budget, int connections, Func<HttpClient> createClient) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        long bytes = 0, reserved = 0; string? error = null; var clock = Stopwatch.StartNew();
        long Reserve(long amount) { while (true) { long old = Interlocked.Read(ref reserved), available = Math.Min(amount, budget - old); if (available <= 0) return 0; if (Interlocked.CompareExchange(ref reserved, old + available, old) == old) return available; } }
        var tasks = Enumerable.Range(0, connections).Select(async _ => {
            using var client = createClient(); byte[] buffer = new byte[upload ? 2 * 1048576 : 65536]; if (upload) RandomNumberGenerator.Fill(buffer);
            int uploadSize = 65536;
            try {
                while (!deadline.IsCancellationRequested) {
                    if (upload) {
                        int size = (int)Reserve(uploadSize); if (size == 0) break;
                        using var content = new ByteArrayContent(buffer, 0, size);
                        using var request = new HttpRequestMessage(HttpMethod.Post, server.Upload) { Content = content };
                        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                        if ((int)response.StatusCode == 413 && uploadSize > 8192) { uploadSize = Math.Max(8192, uploadSize / 2); continue; }
                        response.EnsureSuccessStatusCode(); await using var input = await response.Content.ReadAsStreamAsync(deadline.Token); string text = (await SmallResponse(input, deadline.Token)).Trim();
                        if (text != "size=" + size) throw new IOException("Servidor não confirmou o tamanho do upload.");
                        Interlocked.Add(ref bytes, size); uploadSize = Math.Min(uploadSize * 2, buffer.Length);
                    } else {
                        long limit = Reserve(server.Size); if (limit == 0) break;
                        using var response = await client.GetAsync(server.Download, HttpCompletionOption.ResponseHeadersRead, deadline.Token); response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength != server.Size) throw new IOException("Tamanho do download não corresponde ao anunciado.");
                        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token); long remaining = limit;
                        while (remaining > 0) {
                            int n = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), deadline.Token);
                            if (n == 0) throw new IOException("Download incompleto.");
                            remaining -= n; Interlocked.Add(ref bytes, n);
                        }
                    }
                    progress?.Report(new(upload ? "Upload" : "Download", Interlocked.Read(ref bytes) * 8 / clock.Elapsed.TotalSeconds / 1000000, Interlocked.Read(ref bytes)));
                }
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception e) when (e is HttpRequestException or IOException) { Interlocked.CompareExchange(ref error, e.Message, null); }
        }).ToArray();
        await Task.WhenAll(tasks); ct.ThrowIfCancellationRequested();
        long total = Interlocked.Read(ref bytes);
        if (total == 0) throw new IOException(error ?? "Nenhum dado foi confirmado nesta fase.");
        // Any failed stream invalidates a complete-looking result. Preserve download if upload fails.
        if (error is not null) throw new IOException(error);
        double rate = Math.Round(total * 8 / clock.Elapsed.TotalSeconds / 1000000, 2);
        progress?.Report(new(upload ? "Upload concluído" : "Download concluído", rate, total)); return rate;
    }
}
