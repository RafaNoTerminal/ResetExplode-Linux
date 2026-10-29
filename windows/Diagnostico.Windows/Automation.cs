using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Diagnostico.Windows;

public record RegistryBackup(bool Machine, string Path, string Name, bool Exists, RegistryValueKind Kind, string? Value);
public record PowerBackup(Guid Plan, string Subgroup, string Setting, uint Value);
public record SetupBackup(string Application, string Computer, string UserSid, DateTimeOffset Date, string[] Actions, RegistryBackup[] Registry, PowerBackup[] Power);
public record ChangePreview(string Acao, string Escopo, string EstadoAtual, string Alteracao);

public static class Automation
{
    public static bool Admin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static string BackupDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RBT-Diagnostico", "Backups");
    static RegistryKey Root(bool machine) => machine ? Registry.LocalMachine : Registry.CurrentUser;
    public static void Validate(SetupAction[] actions) {
        AutomationCatalog.Select(actions.Select(a => a.Id));
        if (actions.Any(a => a.Admin) && !Admin) throw new UnauthorizedAccessException("A seleção requer administrador. Feche e abra novamente o aplicativo confirmando a solicitação do Windows.");
        if (Environment.OSVersion.Version.Build >= 22000 && actions.Any(a => a.Windows10Only)) throw new InvalidOperationException("A seleção contém ajustes exclusivos do Windows 10. Desmarque-os neste Windows 11.");
    }
    static RegistryBackup Capture(RegistryChange change) {
        using var key = Root(change.Machine).OpenSubKey(change.Path);
        bool exists = key?.GetValueNames().Contains(change.Name, StringComparer.OrdinalIgnoreCase) == true;
        if (!exists) return new(change.Machine, change.Path, change.Name, false, RegistryValueKind.None, null);
        var kind = key!.GetValueKind(change.Name); object? value = key.GetValue(change.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        string? text = kind switch { RegistryValueKind.Binary => Convert.ToHexString((byte[])value!), RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])value!), _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) };
        return new(change.Machine, change.Path, change.Name, true, kind, text);
    }
    static object Decode(RegistryBackup backup) => backup.Kind switch {
        RegistryValueKind.DWord => int.Parse(backup.Value!, System.Globalization.CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(backup.Value!, System.Globalization.CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.FromHexString(backup.Value!), RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(backup.Value!)!, _ => backup.Value ?? ""
    };
    static SetupBackup Capture(SetupAction[] actions) {
        var registry = actions.SelectMany(a => a.Registry ?? []).Select(Capture).ToArray();
        var power = actions.SelectMany(a => a.Power ?? []).Select(change => { var plan = Power.Active(); return new PowerBackup(plan, change.Subgroup, change.Setting, Power.Read(plan, change.Subgroup, change.Setting)); }).ToArray();
        return new(Brand.Name, Environment.MachineName, WindowsIdentity.GetCurrent().User!.Value, DateTimeOffset.Now, actions.Select(a => a.Id).ToArray(), registry, power);
    }
    public static async Task<List<ChangePreview>> Preview(SetupAction[] actions, CancellationToken ct) {
        Validate(actions); var backup = Capture(actions); var rows = new List<ChangePreview>();
        foreach (var action in actions) {
            rows.Add(new(action.Title, action.Scope, "Selecionado", action.Description));
            if (action.Operation == "onedrive") {
                var snapshot = await Task.Run(OneDriveDetection.Read, ct); var plan = OneDriveUninstall.Plan(snapshot, File.Exists);
                rows.Add(new(action.Title, plan.Scope, plan.State, plan.Details));
            }
            foreach (var change in action.Registry ?? []) {
                var prior = backup.Registry.First(p => p.Machine == change.Machine && p.Path == change.Path && p.Name == change.Name);
                rows.Add(new(action.Title, (change.Machine ? "HKLM\\" : "HKCU\\") + change.Path + "\\" + change.Name, prior.Exists ? prior.Value ?? "" : "Valor ausente", change.Delete ? "Remover entrada" : change.Value));
            }
            foreach (var change in action.Power ?? []) {
                var prior = backup.Power.First(p => p.Setting == change.Setting);
                rows.Add(new(action.Title, "Plano " + prior.Plan + " · AC (tomada)", prior.Value.ToString(), change.Value.ToString()));
            }
        }
        if (actions.Any(a => a.AppPattern is not null)) {
            string packages = await Packages(actions, false, ct);
            using var json = JsonDocument.Parse(packages);
            foreach (var row in json.RootElement.EnumerateArray()) rows.Add(new("Pacote Appx", row.GetProperty("Escopo").ToString(), row.GetProperty("Pacote").ToString(), row.GetProperty("Estado").ToString() + " " + row.GetProperty("Detalhes").ToString()));
        }
        return rows;
    }
    public static async Task<TestResult> Apply(SetupAction[] actions, CancellationToken ct) {
        Validate(actions); ct.ThrowIfCancellationRequested();
        var backup = Capture(actions);
        Directory.CreateDirectory(BackupDirectory);
        string file = Path.Combine(BackupDirectory, "ajustes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        await File.WriteAllTextAsync(file, Reports.Json(backup), new UTF8Encoding(false), ct);
        // The backup is durable before any change. Each individual failure remains visible.
        var details = new Dictionary<string, object?> { ["Backup dos ajustes"] = file, ["Observação"] = "Registro/energia/inicialização podem ser restaurados. Apps removidos precisam de reinstalação. Alguns ajustes exigem nova sessão/reinício manual." };
        var steps = new List<OperationRow>();
        string Error(Exception e) => e.Message + $" (HRESULT 0x{e.HResult:X8})";
        async Task<TestResult> Save(string state) {
            details["Etapas"] = steps.ToArray();
            details["Resumo"] = $"{steps.Count(s => s.Failed)} falha(s); {steps.Count(s => s.Estado == "Preservado")} componente(s) protegido(s) preservado(s); {steps.Count(s => s.Estado == "Ausente")} app(s) já ausente(s).";
            var result = new TestResult("Ajustes do Windows", state, details, DateTimeOffset.Now);
            await File.WriteAllTextAsync(Path.ChangeExtension(file, ".resultado.json"), Reports.Json(result), new UTF8Encoding(false)); return result;
        }
        foreach (var action in actions.Where(a => a.AppPattern is null && a.Id != "provisioned")) {
            if (ct.IsCancellationRequested) { details["Interrupção"] = "As ações já aplicadas estão no backup; as restantes não foram iniciadas."; return await Save("Interrompido parcialmente"); }
            foreach (var change in action.Registry ?? []) {
                string item = (change.Machine ? "HKLM\\" : "HKCU\\") + change.Path + "\\" + change.Name;
                try {
                    using var key = Root(change.Machine).CreateSubKey(change.Path, writable: true);
                    if (change.Delete) key.DeleteValue(change.Name, throwOnMissingValue: false);
                    else key.SetValue(change.Name, change.Kind == "DWord" ? int.Parse(change.Value) : change.Value, change.Kind == "DWord" ? RegistryValueKind.DWord : RegistryValueKind.String);
                    steps.Add(new(action.Title, item, action.Scope, "Aplicado", change.Delete ? "Entrada de inicialização removida, se existia." : "Valor definido: " + change.Value));
                } catch (Exception e) { steps.Add(new(action.Title, item, action.Scope, "Erro", Error(e) + " O valor não pôde ser alterado; permissões ou políticas do Windows podem impedir a gravação mesmo como administrador.")); }
            }
            foreach (var change in action.Power ?? []) {
                try {
                    var prior = backup.Power.First(p => p.Setting == change.Setting); Power.Write(prior.Plan, change.Subgroup, change.Setting, change.Value);
                    steps.Add(new(action.Title, change.Setting, action.Scope, "Aplicado", "Índice AC: " + change.Value));
                } catch (Exception e) { steps.Add(new(action.Title, change.Setting, action.Scope, "Erro", Error(e))); }
            }
            if (action.Operation == "onedrive") {
                try {
                    steps.Add(await OneDriveDetection.Uninstall(CancellationToken.None));
                } catch (Exception e) { steps.Add(new(action.Title, "OneDrive", action.Scope, "Erro", Error(e))); }
            }
        }
        if (ct.IsCancellationRequested) { details["Interrupção"] = "As etapas restantes não foram iniciadas."; return await Save("Interrompido parcialmente"); }
        if (actions.Any(a => a.AppPattern is not null)) {
            try {
                string text = await Packages(actions, true, CancellationToken.None);
                steps.AddRange(OperationResults.Packages(text));
            } catch (Exception e) { steps.Add(new("Remover apps", "Execução PowerShell / retorno do Windows", "Seleção de apps", "Erro", Error(e))); }
        }
        // Store the execution log next to the prior-state backup; never overwrite the prior state.
        return await Save(steps.Any(s => s.Failed) ? "Concluído com falhas; conferir detalhes" : "Concluído");
    }
    public static async Task<TestResult> Restore(string file) {
        var b = JsonSerializer.Deserialize<SetupBackup>(await File.ReadAllTextAsync(file)) ?? throw new IOException("Backup inválido.");
        if (b.Application != Brand.Name || b.Computer != Environment.MachineName || b.UserSid != WindowsIdentity.GetCurrent().User!.Value) throw new IOException("Backup pertence a outro aplicativo, computador ou perfil. Abra pelo mesmo perfil e PC de origem.");
        var actions = AutomationCatalog.Select(b.Actions); Validate(actions);
        foreach (var r in b.Registry) if (!AutomationCatalog.RestorableRegistry(actions).Any(c => c.Machine == r.Machine && c.Path == r.Path && c.Name == r.Name)) throw new IOException("Backup contém chave fora da seleção permitida.");
        foreach (var p in b.Power) if (!actions.SelectMany(a => a.Power ?? []).Any(c => c.Subgroup == p.Subgroup && c.Setting == p.Setting)) throw new IOException("Backup contém energia fora da seleção permitida.");
        var errors = new List<string>();
        foreach (var r in b.Registry.Reverse()) {
            try { using var key = Root(r.Machine).CreateSubKey(r.Path, writable: true); if (r.Exists) key.SetValue(r.Name, Decode(r), r.Kind); else key.DeleteValue(r.Name, false); }
            catch (Exception e) { errors.Add(r.Name + ": " + e.Message); }
        }
        foreach (var p in b.Power.Reverse()) { try { Power.Write(p.Plan, p.Subgroup, p.Setting, p.Value); } catch (Exception e) { errors.Add(p.Setting + ": " + e.Message); } }
        return new("Restaurar ajustes", errors.Count == 0 ? "Concluído" : "Concluído com falhas", new() { ["Backup"] = file, ["Falhas"] = errors, ["Limite"] = "Apps Appx/OneDrive removidos precisam de reinstalação; nenhuma reinstalação é executada por esta restauração." }, DateTimeOffset.Now);
    }
    public static async Task<string> Packages(SetupAction[] actions, bool apply, CancellationToken ct) {
        string script = AutomationCatalog.PackageScript(actions, apply);
        return await RunProcess(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], ct);
    }
    static async Task<string> RunProcess(string executable, string[] arguments, CancellationToken ct) {
        var result = await CaptureProcess(executable, arguments, ct);
        if (result.ExitCode != 0) throw new IOException("Ferramenta " + executable + " retornou " + result.Code + ": " + result.Error);
        return result.Output;
    }
    internal static async Task<ProcessOutcome> CaptureProcess(string executable, string[] arguments, CancellationToken ct) {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        // PowerShell 7 callers may pass incompatible module paths to Windows PowerShell 5.1.
        if (executable.Equals(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), StringComparison.OrdinalIgnoreCase))
            start.Environment["PSModulePath"] = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "Modules") + ";" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsPowerShell", "Modules");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar a ferramenta do Windows.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(ct); } catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        string text = await output, failure = await error;
        return new(process.ExitCode, text.TrimStart('\uFEFF').Trim(), failure);
    }
    // Windows-only fixture; calls the same snapshot codec with isolated test keys, never real settings.
    public static void VerifyRegistryCodec() {
        string path = @"SOFTWARE\RBT-Diagnostico-Tests\" + Guid.NewGuid().ToString("N");
        try {
            using (var key = Registry.CurrentUser.CreateSubKey(path)) { key.SetValue("Test", "before", RegistryValueKind.ExpandString); }
            var r = Capture(new RegistryChange(false, path, "Test", "after", "String"));
            var roundtrip = JsonSerializer.Deserialize<RegistryBackup>(Reports.Json(r))!;
            using (var key = Registry.CurrentUser.OpenSubKey(path, true)) { key!.SetValue("Test", "after"); key.SetValue("Test", Decode(roundtrip), roundtrip.Kind); if ((string?)key.GetValue("Test") != "before" || key.GetValueKind("Test") != RegistryValueKind.ExpandString) throw new Exception("Codec de restauração falhou."); }
        } finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }
}

public static class Power
{
    public static Guid Active() { Check(PowerGetActiveScheme(IntPtr.Zero, out var ptr)); try { return Marshal.PtrToStructure<Guid>(ptr); } finally { LocalFree(ptr); } }
    public static uint Read(Guid plan, string subgroup, string setting) { Guid sub = new(subgroup), key = new(setting); Check(PowerReadACValueIndex(IntPtr.Zero, ref plan, ref sub, ref key, out uint value)); return value; }
    public static void Write(Guid plan, string subgroup, string setting, uint value) { Guid sub = new(subgroup), key = new(setting); Check(PowerWriteACValueIndex(IntPtr.Zero, ref plan, ref sub, ref key, value)); if (Active() == plan) Check(PowerSetActiveScheme(IntPtr.Zero, ref plan)); }
    static void Check(uint error) { if (error != 0) throw new Win32Exception((int)error); }
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
}
