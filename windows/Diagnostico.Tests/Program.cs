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
var getHelp = AutomationCatalog.All.Single(a => a.Id == "app:Microsoft.GetHelp");
Check(!OperationResults.StillPresent(getHelp, [new("App", "Microsoft.GetHelp_1.0_fixture", "Perfil atual", "Removido", "")]), "App removido permaneceu na lista");
Check(!OperationResults.StillPresent(getHelp, [new("App", "Microsoft.GetHelp", "Perfil atual", "Ausente", ""), new("App", "Microsoft.GetHelp", "Novos perfis", "Ausente", "")]), "App ausente permaneceu na lista");
Check(OperationResults.StillPresent(getHelp, [new("App", "Microsoft.GetHelp_1.0_fixture", "Perfil atual", "Erro", "bloqueado")]), "App com erro sumiu da lista");
Check(OperationResults.StillPresent(getHelp, [new("App", "Microsoft.GetHelp", "Perfil atual", "Ausente", ""), new("App", "Microsoft.GetHelp_1.0_fixture", "Novos perfis", "Encontrado", "")]), "App ainda provisionado desapareceu");
Check(OperationResults.StillPresent(AutomationCatalog.All.Single(a => a.Id == "app:Microsoft.Xbox*"), [new("App", "Microsoft.XboxGameCallableUI_1.0_fixture", "Perfil atual", "Preservado", "")]), "Componente protegido sumiu da lista");
Check(OperationResults.StillPresent(getHelp, []), "Consulta ausente foi tratada como remoção confirmada");
var taskbar = AutomationCatalog.All.Single(a => a.Id == "taskbar");
Check(taskbar.Registry!.All(r => r.Name != "ShellFeedsTaskbarViewMode"), "Nova execução ainda escreve na chave Feeds bloqueada");
var feeds = AutomationCatalog.All.Single(a => a.Id == "feeds");
var feedsChange = feeds.Registry!.Single();
Check(feeds.Admin && feeds.Windows10Only && feedsChange.Machine && feedsChange.Path.EndsWith(@"Windows\Windows Feeds") && feedsChange.Name == "EnableFeeds" && feedsChange.Value == "0", "Política Feeds incorreta ou escopo oculto");
Check(AutomationCatalog.RestorableRegistry([taskbar]).Any(r => r.Name == "ShellFeedsTaskbarViewMode") && !AutomationCatalog.RestorableRegistry([feeds]).Any(r => r.Name == "ShellFeedsTaskbarViewMode"), "Backups antigos perderam compatibilidade ou chave legada escapou de escopo");
var laptop = DeviceProfile.Detect("ASUSTeK COMPUTER INC.", "Vivobook", 2, [10]);
var desktop = DeviceProfile.Detect("Dell", "OptiPlex", 1, [3]);
var unknown = DeviceProfile.Detect("Unknown", "Unknown", 0, []);
Check(laptop.Portable && laptop.Asus && !desktop.Portable, "Detecção notebook/desktop incorreta");
Check(DeviceProfile.Detect("ASUS", "Conversível", 0, [31]).Portable, "Conversível não reconhecido");
Check(unknown.Kind == "Tipo não confirmado", "Chassi ausente virou desktop automaticamente");
Check(!laptop.Suggested(true).Contains("power-ac") && desktop.Suggested(true).Contains("power-ac"), "Perfil portátil herdou energia de desktop");
Check(!unknown.Suggested(true).Contains("power-ac") && !laptop.Suggested(false).Contains("feeds"), "Preset desconhecido/Windows11 recebeu ajuste incompatível");
Check(!laptop.Suggested(true).Any(id => id is "usb-ac" or "pcie-ac" or "drivers-wu"), "Ajustes de teste viraram padrão automático");
Check(AutomationCatalog.Select(laptop.Suggested(true)).Length == laptop.Suggested(true).Length, "Preset contém ação inválida");
Check(AutomationCatalog.All.Any(a => a.AppPattern == "Microsoft.OutlookForWindows") && AutomationCatalog.All.Any(a => a.AppPattern == "Microsoft.549981C3F5F10"), "Outlook/Cortana ausentes");
var fixtureUpdates = new[] { new UpdateChoice("00000000-0000-0000-0000-000000000001", 1, "Fixture", true, 1, "Terms") };
Check(WindowsUpdateScript.Build().StartsWith("$apply=$false") && WindowsUpdateScript.Build(fixtureUpdates).StartsWith("$apply=$true"), "Busca de atualizações misturada com instalação");
await Reject<ArgumentException>(() => Task.FromResult(WindowsUpdateScript.Build([fixtureUpdates[0] with { Id = "';iex malicious" }])));
await Reject<ArgumentException>(() => Task.FromResult(WindowsUpdateScript.Build([])));
Check(WindowsUpdateScript.Body.Contains("RebootRequired") && !WindowsUpdateScript.Body.Contains("Restart-Computer") && WindowsUpdateScript.Body.Contains("Type='Software'"), "Atualizações reiniciam ou incluem drivers automaticamente");
var updateRows = OperationResults.Packages("[{\"Acao\":\"Instalar atualização\",\"Pacote\":\"KB fixture\",\"Estado\":\"Erro\",\"Detalhes\":\"0x80070005\",\"Escopo\":\"Computador\"}]");
Check(updateRows.Single().Acao == "Instalar atualização" && updateRows.Single().Failed, "Resultado da atualização virou remoção ou perdeu erro");
var unlicensed = new ActivationSnapshot("Windows 10", "Professional", 19045, true, false, true, [], [], []);
var licensed = unlicensed with { WindowsLicensed = true };
Check(ActivationFlow.Arguments(ActivationMethod.WindowsHwid).Contains("/HWID-NoEditionChange") && !ActivationFlow.Arguments(ActivationMethod.OfficeOhook).Contains("/Z-Windows"), "Escopo de ativação alterou edição ou outro produto");
Check(!ActivationFlow.Clean("\u001b[32mKEY ABCDE-FGHIJ-KLMNO-PQRST-UVWXY\u001b[0m").Contains("ABCDE-"), "Chave de produto vazou no log");
await Reject<ArgumentException>(() => Task.Run(() => ActivationFlow.Validate([], unlicensed)));
await Reject<ArgumentException>(() => Task.Run(() => ActivationFlow.Validate([ActivationMethod.OfficeOhook, ActivationMethod.RemoveOfficeOhook], unlicensed)));
await Reject<InvalidOperationException>(() => Task.Run(() => ActivationFlow.Validate([ActivationMethod.WindowsHwid], unlicensed with { ClientWindows = false })));
await Reject<InvalidOperationException>(() => Task.Run(() => ActivationFlow.Validate([ActivationMethod.OfficeOhook], unlicensed with { OfficeInstalled = false })));
await Reject<InvalidOperationException>(() => Task.Run(() => ActivationFlow.Validate([ActivationMethod.OfficeOhook], unlicensed with { OpenOfficeApps = ["WINWORD"] })));
int activationCalls = 0;
var activation = await ActivationFlow.Execute([ActivationMethod.WindowsHwid], () => Task.FromResult(licensed), _ => { activationCalls++; return Task.FromResult(new ActivationRun(0, "", "")); });
Check(activationCalls == 0 && OperationResults.Read(activation).Single().Estado == "Já licenciado", "Windows já licenciado foi alterado");
activation = await ActivationFlow.Execute([ActivationMethod.WindowsHwid], () => Task.FromResult(unlicensed), _ => Task.FromResult(new ActivationRun(0, "success", "")));
Check(OperationResults.Read(activation).Single().Failed, "Retorno zero declarou Windows ativado sem confirmação");
bool changed = false;
activation = await ActivationFlow.Execute([ActivationMethod.WindowsHwid], () => Task.FromResult(changed ? licensed : unlicensed), _ => { changed = true; return Task.FromResult(new ActivationRun(0, "done", "")); });
Check(OperationResults.Read(activation).Single().Estado == "Licenciado", "Windows licenciado após execução não confirmado");
changed = false;
activation = await ActivationFlow.Execute([ActivationMethod.WindowsHwid], () => Task.FromResult(changed ? licensed : unlicensed), _ => { changed = true; return Task.FromResult(new ActivationRun(1, "", "timeout: input redirected")); });
Check(OperationResults.Read(activation).Single().Estado == "Licenciado", "Retorno do menu/timeout do batch mascarou consulta Windows licenciada");
activation = await ActivationFlow.Execute([ActivationMethod.OfficeOhook], () => Task.FromResult(unlicensed), _ => Task.FromResult(new ActivationRun(1, "Office is permanently activated.", "timeout: input redirected")));
Check(OperationResults.Read(activation).Single().Estado == "Informado pelo motor", "Retorno não contratual do batch mascarou resultado informado do Office");

activation = await ActivationFlow.Execute([ActivationMethod.OfficeOhook], () => Task.FromResult(unlicensed), _ => Task.FromResult(new ActivationRun(0, "Office is permanently activated.", "")));
Check(OperationResults.Read(activation).Single().Estado == "Informado pelo motor" && activation.Estado.Contains("pendente"), "Ohook foi confundido com licença WMI confirmada");
activation = await ActivationFlow.Execute([ActivationMethod.OfficeOhook], () => Task.FromResult(unlicensed), _ => Task.FromResult(new ActivationRun(0, "Some errors were detected.", "")));
Check(OperationResults.Read(activation).Single().Failed, "Falha Office retornando zero foi ignorada");
var profile = new CompanyProfile(@"\\server.example\share\Helpdesk", [new("TEST", "Printer fixture", "192.0.2.20")], [new("Test", "Tools", "Tools/test.exe", CompanyToolKind.Installer)]);
profile.Validate(); Check(profile.Resolve("Tools/test.exe").EndsWith(@"Helpdesk\Tools\test.exe") && profile.Share == @"\\server.example\share", "Perfil de empresa perdeu raiz de compartilhamento");
foreach (string invalid in new[] { "../secret", @"..\secret", @"C:\test.exe", @"\\other\share\tool.exe", "tools/../evil.exe" }) await Reject<ArgumentException>(() => Task.Run(() => CompanyProfile.ValidateRelative(invalid)));
await Reject<ArgumentException>(() => Task.Run(() => CompanyProfile.RootParts("https://example.com")));
string printerInstall = PrinterScripts.Install(profile.Printers, "Fixture driver");
Check(!printerInstall.Contains("Remove-Printer") && printerInstall.Contains("$default.Count -eq 1") && printerInstall.Contains("Já existe"), "Instalação destrói filas, portas ou padrão");
Check(PrinterScripts.MigrateRicoh("fixture.json").Contains("RICOH SP 377SFNwX PCL 6") && !PrinterScripts.MigrateRicoh("fixture.json").Contains("Remove-Printer"), "Migração Ricoh perdeu o escopo/preservação");
await Reject<ArgumentException>(() => Task.Run(() => PrinterScripts.Install([], "Fixture driver")));
Check(MaintenanceCatalog.All.All(a => a.File is "sfc.exe" or "dism.exe" or "ipconfig.exe") && !MaintenanceCatalog.All.SelectMany(a => a.Arguments).Contains("/ResetBase"), "Manutenção usa comando fora do catálogo ou limpeza irrestrita");
await Reject<IOException>(() => NetworkDiagnostics.WithTimeout(async ct => { await Task.Delay(1000, ct); return true; }, TimeSpan.FromMilliseconds(5), CancellationToken.None));
using(var cancelled = new CancellationTokenSource()){cancelled.Cancel();await Reject<OperationCanceledException>(()=>NetworkDiagnostics.WithTimeout(async ct=>{await Task.Delay(100,ct);return true;},TimeSpan.FromSeconds(1),cancelled.Token));}
const string installer = @"C:\Program Files\Microsoft OneDrive\26.0\OneDriveSetup.exe";
var absentDrive = new OneDriveSnapshot([], [], []);
var installedDrive = new OneDriveSnapshot([new('"' + installer + "\" /uninstall /allusers", "Computador · todos os usuários", "fixture")], [@"C:\Program Files\Microsoft OneDrive\OneDrive.exe"], []);
Check(OneDriveUninstall.Plan(absentDrive, _ => true).State == "Ausente", "Stub de fábrica confundido com app instalado");
var drivePlan = OneDriveUninstall.Plan(installedDrive, _ => true);
Check(drivePlan.Executable == installer && drivePlan.Arguments.SequenceEqual(["/uninstall", "/allusers"]), "Comando registrado alterado");
Check(OneDriveUninstall.Plan(installedDrive, _ => false).State == "Erro", "Desinstalador ausente não detectado");
Check(OneDriveUninstall.Plan(new([], installedDrive.Clients, []), _ => true).Executable is null, "Cliente sem registro usou fallback de fábrica");
foreach (string unsafeCommand in new[] { "cmd.exe /c anything", '"' + installer + "\" /uninstall & calc.exe", @"C:\Temp\other.exe /uninstall", @"OneDriveSetup.exe /uninstall" })
    Check(OneDriveUninstall.Plan(new([new(unsafeCommand, "Perfil atual", "fixture")], [], []), _ => true).Executable is null, "Comando não permitido aceito");
Check(OneDriveUninstall.Plan(new(installedDrive.Registrations.Concat([new(@"C:\User\OneDriveSetup.exe /uninstall", "Perfil atual", "fixture2")]).ToArray(), installedDrive.Clients, []), _ => true).Executable is null, "Instalação ambígua removida sem seleção");
Check(OneDriveUninstall.Plan(new([], [], ["consulta negada"]), _ => true).State == "Erro", "Falha de detecção confundida com app ausente");
int uninstallCalls = 0;
var driveResult = await OneDriveUninstall.Execute(() => Task.FromResult(absentDrive), _ => true, _ => Task.FromResult(true), _ => { uninstallCalls++; return Task.FromResult(new ProcessOutcome(0, "", "")); }, CancellationToken.None, 1);
Check(driveResult.Estado == "Ausente" && uninstallCalls == 0, "Executou desinstalador sem instalação");
driveResult = await OneDriveUninstall.Execute(() => Task.FromResult(installedDrive), _ => true, _ => Task.FromResult(false), _ => { uninstallCalls++; return Task.FromResult(new ProcessOutcome(0, "", "")); }, CancellationToken.None, 1);
Check(driveResult.Failed && uninstallCalls == 0, "Executável sem assinatura confirmada foi iniciado");
bool removedDrive = false;
driveResult = await OneDriveUninstall.Execute(() => Task.FromResult(removedDrive ? absentDrive : installedDrive), _ => true, _ => Task.FromResult(true), _ => { removedDrive = true; return Task.FromResult(new ProcessOutcome(0, "", "")); }, CancellationToken.None, 1);
Check(driveResult.Estado == "Removido", "Ausência após desinstalar não foi confirmada");
driveResult = await OneDriveUninstall.Execute(() => Task.FromResult(installedDrive), _ => true, _ => Task.FromResult(true), _ => Task.FromResult(new ProcessOutcome(0, "", "")), CancellationToken.None, 1);
Check(driveResult.Failed, "Retorno zero virou sucesso com cliente ainda presente");
driveResult = await OneDriveUninstall.Execute(() => Task.FromResult(installedDrive), _ => true, _ => Task.FromResult(true), _ => Task.FromResult(new ProcessOutcome(-2147219813, "", "erro fixture")), CancellationToken.None, 1);
Check(driveResult.Failed && driveResult.Detalhes.Contains("0x8004069B") && driveResult.Detalhes.Contains(installer), "Erro nativo perdeu código ou caminho");
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

// Diagnostics can report corruption with a nonzero SFC exit; preserve the finding
// and suggest servicing instead of interpreting every nonzero exit as "no result".
Check(MaintenanceAssessment.Read("sfc-verify", 1, "A Proteção de Recursos do Windows encontrou violações de integridade.").Suggested.SequenceEqual(new[] { "sfc-repair" }), "Violação SFC não ofereceu correção");
Check(MaintenanceAssessment.Read("sfc-repair", 0, "Windows Resource Protection found corrupt files but was unable to fix some of them.").Suggested.SequenceEqual(new[] { "dism-repair", "sfc-repair" }), "SFC incompleto perdeu sugestão DISM/SFC");
Check(MaintenanceAssessment.Read("sfc-repair", 0, "A Proteção de Recursos do Windows encontrou arquivos corrompidos e os reparou com êxito.").Suggested.Length == 0, "Reparo confirmado gerou nova correção automática");
Check(MaintenanceAssessment.Read("sfc-verify", 0, "Windows Resource Protection did not find any integrity violations.").State == "Sem violações no SFC", "SFC sem violações classificado incorretamente");
Check(MaintenanceAssessment.Read("dism-scan", 0, "The component store is repairable.").Suggested.Contains("dism-repair"), "DISM reparável não oferece RestoreHealth");
Check(MaintenanceAssessment.Read("dism-scan", 0, "The component store cannot be repaired.").Suggested.Length == 0, "DISM não reparável propôs reparo cego");
Check(MaintenanceAssessment.Read("sfc-verify", 5, "Access denied").State == "Erro na execução", "Erro de execução ignorado");
Check(MaintenanceAssessment.Read("sfc-verify", 0, "Unknown localized output").State.Contains("conferir"), "Saída desconhecida aprovada sem evidência");
var onboarding = OnboardingProfile.Default; onboarding.Validate();
Check(onboarding.Sectors.Single().Items.Count(i => i.Kind == OnboardingKind.Download) == 8, "Apps do técnico interno não correspondem ao Mint");
Check(onboarding.Sectors.Single().Items.Where(i => i.Kind == OnboardingKind.Printer).Count() == 0, "Impressoras de setor foram presumidas");
await Reject<ArgumentException>(() => Task.FromResult(OnboardingProfile.WebUri("https://user:secret@example.com")));
await Reject<ArgumentException>(() => Task.FromResult(OnboardingProfile.WebUri("file:///C:/Windows")));
var receipt = new CompanyPrinter("TST", "TST_IMP_Comprovantes", "192.0.2.22", Receipt: true);
await Reject<ArgumentException>(() => Task.FromResult(PrinterScripts.Install([receipt], "Ricoh driver")));
Check(PrinterScripts.Install([receipt], "ELGIN i9").Contains("Add-Printer"), "Driver Elgin correto rejeitado");
Check(PrinterScripts.ConfigureReceipts([receipt], "fixture").Contains("72000"), "Preferências de comprovante ausentes");
// Opaque driver preferences must retain every proprietary byte, across queue names.
byte[] devMode = new byte[228];
System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(devMode.AsSpan(68), 220);
System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(devMode.AsSpan(70), 8);
devMode[220] = 0x51; devMode[227] = 0x9A;
var receiptModel = new ReceiptProfile(1, "ELGIN i9(USB)", new string('a', 64), devMode, true, DateTimeOffset.Now); receiptModel.Validate();
var queueMode = ReceiptProfile.ForQueue(devMode, "TST_IMP_Comprovantes");
Check(ReceiptProfile.SameSettings(devMode, queueMode) && devMode[0] == 0 && queueMode[0] != 0, "Transferência perdeu preferências privadas ou mudou origem");
Check(queueMode.AsSpan(220).SequenceEqual(devMode.AsSpan(220)), "Dados privados de corte/logo foram truncados");
await Reject<ArgumentException>(() => Task.Run(() => receiptModel.Compatible("ELGIN i9(USB)", new string('b', 64))));
await Reject<ArgumentException>(() => Task.Run(() => (receiptModel with { ConfirmedByTechnician = false }).Validate()));
await Reject<ArgumentException>(() => Task.Run(() => ReceiptProfile.ValidateMode(devMode[..225])));
byte[]? savedGlobal = null, savedUser = null; bool restored = false;
ReceiptPreferenceTransaction.Apply(queueMode, level => level == 8 ? savedGlobal : savedUser, (level, mode) => { if(level == 8) savedGlobal = mode; else savedUser = mode; }, () => restored = true);
Check(!restored && ReceiptProfile.SameSettings(savedGlobal!, queueMode) && ReceiptProfile.SameSettings(savedUser!, queueMode), "Preferências globais/usuário não foram conferidas");
await Reject<IOException>(() => Task.Run(() => ReceiptPreferenceTransaction.Apply(queueMode, level => level == 8 ? savedGlobal : savedUser, (level, mode) => { if(level == 8) savedGlobal = mode; else throw new IOException("fixture per-user write failed"); }, () => { savedGlobal = devMode; restored = true; })));
Check(restored && ReferenceEquals(savedGlobal, devMode), "Falha parcial não restaurou estado anterior");
restored = false;
await Reject<IOException>(() => Task.Run(() => ReceiptPreferenceTransaction.Apply(queueMode, level => { var changed = (byte[])queueMode.Clone(); changed[227] ^= 1; return changed; }, (level, mode) => { }, () => restored = true)));
Check(restored, "Driver que não mantém preferências privadas foi aprovado");
// Exact-package automatic preset, no user capture or saved .dat/profile required.
byte[] originalElgin = new byte[ElginReceiptPreset.ModeLength];
System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(originalElgin.AsSpan(68), 220);
System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(originalElgin.AsSpan(70), 4468);
originalElgin[0x350] = 77; originalElgin[0x3a0] = 0x8F; originalElgin[0x3a4] = 44;
var automatic = ElginReceiptPreset.Build(originalElgin, ElginReceiptPreset.DriverName, ElginReceiptPreset.Fingerprint); automatic.Validate();
Check(!automatic.ConfirmedByTechnician && automatic.Method == "AutomaticPreset", "Preset automático ainda depende de conferência/captura manual");
Check(automatic.DevMode[0x350] == 77 && automatic.DevMode[0x3a4] == 44 && originalElgin[0x3a0] == 0x8F, "Preset alterou origem ou preferências fora do escopo");
Check(automatic.DevMode[0x3a0] == 0x84, "Logo precisa estar habilitado somente no início da página, preservando bits externos");
Check(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(automatic.DevMode.AsSpan(0x33c)) == 8 && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(automatic.DevMode.AsSpan(0x340)) == 1, "Corte fim de página/parcial não corresponde ao codec validado pelo driver");
Check(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(automatic.DevMode.AsSpan(0x7c0)) == 2 && automatic.DevMode[0x7c4] == 48 && automatic.DevMode[0x7c5] == 48 && automatic.DevMode[0x7c8] == 2, "Logo salvo, chaves 0/0 e centralização incorretos");
Check(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(automatic.DevMode.AsSpan(80)) == 1000 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(automatic.DevMode.AsSpan(82)) == 720, "Dimensões não são 72x100 mm");
await Reject<ArgumentException>(() => Task.Run(() => ElginReceiptPreset.Build(originalElgin, "Other driver", ElginReceiptPreset.Fingerprint)));
await Reject<ArgumentException>(() => Task.Run(() => ElginReceiptPreset.Build(originalElgin, ElginReceiptPreset.DriverName, new string('0',64))));
await Reject<ArgumentException>(() => Task.Run(() => ElginReceiptPreset.Build(devMode, ElginReceiptPreset.DriverName, ElginReceiptPreset.Fingerprint)));
foreach (int offset in new[] { 0x33c, 0x340, 0x344, 0x3a0, 0x7c0, 0x7c4, 0x7c5, 0x7c8, 80, 82 }) {
    var changedPreset = (byte[])automatic.DevMode.Clone(); changedPreset[offset] ^= 1;
    await Reject<IOException>(() => Task.Run(() => ElginReceiptPreset.Verify(changedPreset, automatic.Driver, automatic.DriverFingerprint)));
}
// Representation reproduced by the genuine vendor merge routine, without a printer.
var vendorNormalized = (byte[])automatic.DevMode.Clone();
System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(vendorNormalized.AsSpan(72),0x1a513);
System.Text.Encoding.Unicode.GetBytes("\0ustom").CopyTo(vendorNormalized,102);
ElginReceiptPreset.Verify(vendorNormalized,automatic.Driver,automatic.DriverFingerprint);
Check(true,"Tamanho personalizado normalizado pelo driver foi recusado");
foreach(int offset in new[]{76,78,80,82,92,94,0x33c,0x340,0x344,0x3a0,0x7c0,0x7c4,0x7c5,0x7c8}) {
 var wrong=(byte[])vendorNormalized.Clone();wrong[offset]^=1;
 await Reject<IOException>(()=>Task.Run(()=>ElginReceiptPreset.Verify(wrong,automatic.Driver,automatic.DriverFingerprint)));
}
var wrongForm=(byte[])vendorNormalized.Clone();System.Text.Encoding.Unicode.GetBytes("Outro").CopyTo(wrongForm,102);
await Reject<IOException>(()=>Task.Run(()=>ElginReceiptPreset.Verify(wrongForm,automatic.Driver,automatic.DriverFingerprint)));
string? normalizedInput=Environment.GetEnvironmentVariable("RBT_ELGIN_NORMALIZED_INPUT");
if(!string.IsNullOrWhiteSpace(normalizedInput)){ElginReceiptPreset.Verify(File.ReadAllBytes(normalizedInput),automatic.Driver,automatic.DriverFingerprint);Check(true,"Saída da DLL original recusada");}
string? codecOutput = Environment.GetEnvironmentVariable("RBT_ELGIN_CODEC_OUTPUT");
if (!string.IsNullOrWhiteSpace(codecOutput)) File.WriteAllBytes(codecOutput, automatic.DevMode);
var localDriver = new LocalPrinterPackage("Fixture Ricoh", "DRIVERS/FixtureRicoh", "fixture.inf", new string('a',64), "fixture.cat", new string('b',64), ["Fixture Ricoh", "Fixture Ricoh 2"], "Fixture Ricoh");
localDriver.Validate();
Check(localDriver.InstallScript("fixture.inf", "Fixture Ricoh").Contains("/add-driver") && !localDriver.InstallScript("fixture.inf", "Fixture Ricoh").Split("$rows=")[0].Contains(" /install"), "Driver staging inclui atualização de dispositivos");
Check(LocalPrinterPackage.Body.Contains("FileRepository") && LocalPrinterPackage.Body.Contains("Add-PrinterDriver") && LocalPrinterPackage.Body.Contains("PrinterEnvironment"), "Driver não é confirmado no Driver Store/spooler x64");
Check(!LocalPrinterPackage.Body.Contains("Restart-Service") && !LocalPrinterPackage.Body.Contains("Remove-Printer"), "Instalação do driver pode reiniciar spooler/remover filas");
await Reject<ArgumentException>(() => Task.Run(() => (localDriver with { RelativeDirectory = "DRIVERS/../escape" }).Validate()));
await Reject<ArgumentException>(() => Task.Run(() => (localDriver with { InfSha256 = "bad" }).Validate()));
await Reject<ArgumentException>(() => Task.Run(() => localDriver.InstallScript("fixture.inf", "Foreign model")));
int driverInstallCalls = 0; bool driverExists = true;
Check(await PrinterDriverRequirement.Ensure(()=>Task.FromResult(driverExists),()=>{driverInstallCalls++;return Task.FromResult(true);}) && driverInstallCalls==0, "Driver existente foi reinstalado");
driverExists=false;
Check(!await PrinterDriverRequirement.Ensure(()=>Task.FromResult(driverExists),()=>Task.FromResult(false)), "Cancelar instalação não bloqueou filas");
await Reject<IOException>(()=>PrinterDriverRequirement.Ensure(()=>Task.FromResult(false),()=>Task.FromResult(true)));
Check(await PrinterDriverRequirement.Ensure(()=>Task.FromResult(driverExists),()=>{driverExists=true;return Task.FromResult(true);}), "Instalação confirmada não libera filas");
var removalScript=PrinterRemoval.Build([new("Fixture [literal]","Fixture driver","port1",false)],"fixture.json",false);
Check(removalScript.Contains("Remove-Printer -InputObject") && !removalScript.Contains("Remove-PrinterPort") && !removalScript.Contains("Remove-PrinterDriver"), "Exclusão remove porta/driver ou permite wildcard");
Check(removalScript.IndexOf("Set-Content")<removalScript.IndexOf("Remove-Printer -InputObject") && removalScript.Contains("Get-PrintJob"), "Exclusão não faz backup/check de jobs antes de remover");
await Reject<ArgumentException>(()=>Task.FromResult(PrinterRemoval.Build([],"fixture.json",false)));
await Reject<ArgumentException>(()=>Task.FromResult(PrinterRemoval.Build([new("Duplicate","d","p",false),new("Duplicate","d","p",false)],"fixture.json",false)));
Check(LocalPrinterPackage.NativeHelpers.Contains("printui.exe") && LocalPrinterPackage.NativeHelpers.Contains("'/ia'") && !LocalPrinterPackage.NativeHelpers.Contains("'/if'"), "Alternativa Ricoh cria fila antes de registrar driver");
int formReads=0,formCreates=0;
ReceiptPaperForm.Ensure(()=>{if(++formReads==1)throw new System.ComponentModel.Win32Exception(1902);return (72000,100000);},()=>formCreates++);
Check(formReads==2 && formCreates==1,"Papel ausente 1902 não foi criado e relido");
ReceiptPaperForm.Ensure(()=>(72000,100000),()=>throw new Exception("Não recriar papel existente"));
Check(true,"Papel existente não preservado");
await Reject<IOException>(()=>{ReceiptPaperForm.Ensure(()=>(80000,100000),()=>throw new Exception("Não sobrescrever"));return Task.CompletedTask;});
await Reject<System.ComponentModel.Win32Exception>(()=>{ReceiptPaperForm.Ensure(()=>throw new System.ComponentModel.Win32Exception(5),()=>throw new Exception("Não criar após acesso negado"));return Task.CompletedTask;});
await Reject<System.ComponentModel.Win32Exception>(()=>{ReceiptPaperForm.Ensure(()=>throw new System.ComponentModel.Win32Exception(1902),()=>throw new System.ComponentModel.Win32Exception(5));return Task.CompletedTask;});
await Reject<System.ComponentModel.Win32Exception>(()=>{ReceiptPaperForm.Ensure(()=>throw new System.ComponentModel.Win32Exception(1902),()=>{});return Task.CompletedTask;});
Console.WriteLine($"Total final: {passed} verificações aprovadas.");

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
