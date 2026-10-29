using System.Net;
using Diagnostico;

int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
async Task Reject<T>(Func<Task> action) where T : Exception { try { await action(); throw new Exception("Exceção esperada: " + typeof(T).Name); } catch (T) { passed++; } }

var html = Reports.Html(new { Texto = "<script>alert('x')</script>", Nulo = (string?)null });
Check(!html.Contains("<script>"), "Relatório deve escapar HTML"); Check(html.Contains("&lt;script&gt;"), "Conteúdo deve estar no HTML escapado");
Check(Reports.Json(new { Número = 123 }).Contains("123"), "JSON inválido");
Check(AutomationCatalog.All.Select(a => a.Id).Distinct().Count() == AutomationCatalog.All.Count, "IDs de ajustes duplicados");
await Reject<ArgumentException>(() => Task.FromResult(AutomationCatalog.Select(["app:'; malicious"])));
await Reject<ArgumentException>(() => Task.FromResult(AutomationCatalog.Select(["provisioned"])));
var setup = AutomationCatalog.Select(["app:Microsoft.GetHelp", "provisioned", "usb-ac"]);
Check(setup.Where(a => a.Power is not null).All(a => a.Admin), "Energia sem exigência de admin");
string packageScript = AutomationCatalog.PackageScript(setup, apply: false);
Check(packageScript.Contains("$apply=$false") && packageScript.Contains("'Microsoft.GetHelp'"), "Prévia Appx não está limitada à seleção");
Check(!packageScript.Contains("Remove-Item ") && !packageScript.Contains("WindowsStore") && !packageScript.Contains("WindowsCalculator"), "Remoção alcança arquivos pessoais ou apps fora da seleção");
Check(AutomationCatalog.All.Where(a => a.Id is "usb-ac" or "pcie-ac").All(a => a.Registry is null && a.Power!.All(p => p.Value == 0)), "Teste de energia deve usar somente índice AC previsto");
var operation = new TestResult("Ajustes do Windows", "Concluído com falhas", new() { ["Etapas"] = new[] {
    new OperationRow("Barra", "HKCU\\Teste", "Perfil atual", "Erro", "Acesso negado 0x80070005"),
    new OperationRow("Remover app", "Microsoft.XboxGameCallableUI", "Perfil atual", "Preservado", "Componente protegido.") } }, DateTimeOffset.Now);
var operationRows = OperationResults.Read(operation);
Check(operationRows.Count == 2 && operationRows.Count(r => r.Failed) == 1, "Componente preservado confundido com falha");
Check(OperationResults.Summary(operation).Contains("Microsoft.XboxGameCallableUI") && OperationResults.Summary(operation).Contains("0x80070005"), "Resumo da interface perdeu o nome ou motivo da falha");
Check(Reports.Html(operation).Contains("0x80070005") && Reports.Json(operation).Contains("Microsoft.XboxGameCallableUI"), "Erro/item perdido nas exportações");
var legacy = new TestResult("Ajustes do Windows", "Falhas", new() { ["Barra"] = "Erro: Attempted to perform an unauthorized operation.", ["Pacotes Appx"] = new[] { new { Escopo = "Perfil atual", Pacote = "Microsoft.XboxGameCallableUI_fixture", Estado = "Erro", Detalhes = "0x80073CFA / 0x80070032" } } }, DateTimeOffset.Now);
Check(OperationResults.Read(legacy).Count(r => r.Failed) == 2, "As falhas do formato antigo não foram preservadas");
Check(NetworkDiagnostics.ValidateHost(" 1.1.1.1 ") == "1.1.1.1", "IP válido rejeitado");
await Reject<ArgumentException>(() => NetworkDiagnostics.PingHost("example.com; rm", CancellationToken.None));
await Reject<ArgumentException>(() => NetworkDiagnostics.PingHost("https://example.com/", CancellationToken.None));
await Reject<ArgumentOutOfRangeException>(() => Benchmarks.Cpu(0, TimeSpan.FromSeconds(1), CancellationToken.None));
await Reject<ArgumentOutOfRangeException>(() => Benchmarks.Memory(1024, TimeSpan.FromSeconds(1), CancellationToken.None));
var memory = await Benchmarks.Memory(1, TimeSpan.FromMilliseconds(15), CancellationToken.None); Check(memory.Estado == "Concluído", "RAM não concluiu");
using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); await Reject<OperationCanceledException>(() => Benchmarks.Memory(1, TimeSpan.FromSeconds(1), cancelled.Token)); }
var path = Path.Combine(Path.GetTempPath(), "rbt-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
try {
    string preserved = Path.Combine(path, "arquivo-existente.txt"); await File.WriteAllTextAsync(preserved, "preservar");
    var disk = await Benchmarks.Disk(path, 2, CancellationToken.None); Check(disk.Estado == "Concluído", "Disco não concluiu");
    Check(Directory.GetFiles(path).Length == 1 && await File.ReadAllTextAsync(preserved) == "preservar", "Arquivo existente modificado / temporário restante");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    await Reject<OperationCanceledException>(() => Benchmarks.Disk(path, 2, cancelled.Token)); Check(Directory.GetFiles(path).Length == 1, "Temporário sobrou no cancelamento");
} finally { Directory.Delete(path, true); }
Check(!SpeedTest.ValidEndpoint(new Uri("http://example.com")), "HTTP aceito para medição");
Check(!SpeedTest.ValidEndpoint(new Uri("https://user:pass@example.com")), "Credencial no endpoint aceita");
var server = new SpeedServer("Servidor de teste", new("https://fixture.test/download"), new("https://fixture.test/upload"), new("https://fixture.test/latency"), 1048576);
var handler = new FixtureHandler();
Func<HttpClient> factory = () => new(handler, disposeHandler: false);
var speed = await SpeedTest.Run(null, CancellationToken.None, server, seconds: 1, budget: 1048576, connections: 2, createClient: factory);
Check(Convert.ToDouble(speed.Dados["Download (Mbps)"]) > 0 && Convert.ToDouble(speed.Dados["Upload (Mbps)"]) > 0, "Taxas medidas ausentes");
Check(handler.UploadBytes <= 1048576, "Orçamento de upload excedido");
Check(handler.Rejected413 > 0 && handler.AcceptedSizes.All(s => s <= 32768), "Upload não se adaptou ao 413");
handler.BlockUploads = true;
speed = await SpeedTest.Run(null, CancellationToken.None, server, seconds: 1, budget: 1048576, createClient: factory);
Check(Convert.ToDouble(speed.Dados["Download (Mbps)"]) > 0 && speed.Dados["Upload (Mbps)"] is null, "Upload falhou mas apagou download / inventou zero");
handler.BlockUploads = false; handler.WrongSize = true;
await Reject<IOException>(() => SpeedTest.Run(null, CancellationToken.None, server, seconds: 1, budget: 1048576, createClient: factory));
using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); await Reject<OperationCanceledException>(() => SpeedTest.Run(null, cancelled.Token, server, createClient: factory)); }
Console.WriteLine($"OK: {passed} verificações · HTML, validação, memória, disco/limpeza, cancelamento e medição nativa/413/orçamento.");

sealed class FixtureHandler : HttpMessageHandler
{
    public long UploadBytes; public int Rejected413; public bool BlockUploads, WrongSize;
    public System.Collections.Concurrent.ConcurrentBag<int> AcceptedSizes = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        await Task.Delay(10, ct);
        if (request.RequestUri!.AbsolutePath == "/latency") return new(HttpStatusCode.OK) { Content = new StringContent("test=test\n") };
        if (request.Method == HttpMethod.Post) {
            byte[] bytes = await request.Content!.ReadAsByteArrayAsync(ct); Interlocked.Add(ref UploadBytes, bytes.Length);
            if (BlockUploads) return new(HttpStatusCode.Forbidden);
            if (bytes.Length > 32768) { Interlocked.Increment(ref Rejected413); return new(HttpStatusCode.RequestEntityTooLarge); }
            AcceptedSizes.Add(bytes.Length); return new(HttpStatusCode.OK) { Content = new StringContent("size=" + bytes.Length) };
        }
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[WrongSize ? 65536 : 1048576]) };
    }
}
