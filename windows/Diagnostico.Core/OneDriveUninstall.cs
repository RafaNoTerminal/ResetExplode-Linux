using System.Text.RegularExpressions;

namespace Diagnostico;

public record OneDriveRegistration(string Command, string Scope, string RegistryPath);
public record OneDriveSnapshot(OneDriveRegistration[] Registrations, string[] Clients, string[] Errors)
{
    public bool Present => Registrations.Length > 0 || Clients.Length > 0;
}
public record OneDrivePlan(string? Executable, string[] Arguments, string Scope, string State, string Details);
public record ProcessOutcome(int ExitCode, string Output, string Error)
{
    public string Code => $"{ExitCode} (0x{unchecked((uint)ExitCode):X8})";
}

public static class OneDriveUninstall
{
    public static OneDrivePlan Plan(OneDriveSnapshot snapshot, Func<string, bool> exists)
    {
        if (snapshot.Errors.Length > 0) return new(null, [], "Detecção", "Erro", "Não foi possível confirmar a instalação: " + string.Join("; ", snapshot.Errors));
        if (!snapshot.Present) return new(null, [], "Perfil atual e computador", "Ausente", "Nenhuma instalação registrada ou cliente OneDrive encontrado. O instalador de fábrica não é prova de que o OneDrive ainda esteja instalado; nenhuma desinstalação será executada.");
        var registrations = snapshot.Registrations.DistinctBy(r => (r.Command.ToUpperInvariant(), r.Scope)).ToArray();
        if (registrations.Length != 1) return new(null, [], "Detecção", "Erro", registrations.Length == 0
            ? "Cliente OneDrive encontrado sem desinstalador registrado. Confira Microsoft OneDrive em Configurações > Aplicativos; o app não usará um instalador antigo da pasta Windows."
            : "Mais de uma instalação do OneDrive foi encontrada. Use Configurações > Aplicativos para escolher a instalação correta; nenhuma foi removida automaticamente.");
        var record = registrations[0];
        var match = Regex.Match(record.Command.Trim(), "^(?:\"([^\"\r\n]+\\.exe)\"|([^\"\r\n]+?\\.exe))(?:[ \\t]+(.*))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return new(null, [], record.Scope, "Erro", "Comando de desinstalação não reconhecido. Use Configurações > Aplicativos. Registro: " + record.RegistryPath);
        string executable = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        var arguments = Regex.Split(match.Groups[3].Value.Trim(), "[ \\t]+").Where(a => a.Length > 0).ToArray();
        bool rooted = Regex.IsMatch(executable, @"^[A-Za-z]:[\\/]");
        string filename = executable.Replace('\\', '/').Split('/').Last();
        if (!rooted || filename.Equals("OneDriveSetup.exe", StringComparison.OrdinalIgnoreCase) == false
            || executable.Split(['\\', '/']).Any(p => p == "..")
            || !arguments.Contains("/uninstall", StringComparer.OrdinalIgnoreCase)
            || arguments.Any(a => !new[] { "/uninstall", "/allusers", "/silent" }.Contains(a, StringComparer.OrdinalIgnoreCase)))
            return new(null, [], record.Scope, "Erro", "O comando registrado não corresponde a uma desinstalação direta reconhecida do OneDrive. Use Configurações > Aplicativos. Registro: " + record.RegistryPath);
        if (!exists(executable)) return new(null, [], record.Scope, "Erro", "O desinstalador registrado não existe: " + executable + ". A instalação pode estar incompleta; confira-a em Configurações > Aplicativos.");
        return new(executable, arguments, record.Scope, "Encontrado", "Desinstalador registrado: " + executable + " " + string.Join(" ", arguments) + ". A assinatura Microsoft será verificada antes da execução. Arquivos sincronizados e pastas pessoais não serão excluídos pelo app.");
    }

    public static async Task<OperationRow> Execute(Func<Task<OneDriveSnapshot>> detect, Func<string, bool> exists,
        Func<string, Task<bool>> microsoftSigned, Func<OneDrivePlan, Task<ProcessOutcome>> run, CancellationToken ct, int verifyAttempts = 20)
    {
        ct.ThrowIfCancellationRequested();
        var plan = Plan(await detect(), exists);
        if (plan.Executable is null) return new("Desinstalar o cliente OneDrive", "OneDrive", plan.Scope, plan.State, plan.Details);
        if (!await microsoftSigned(plan.Executable)) return new("Desinstalar o cliente OneDrive", plan.Executable, plan.Scope, "Erro", "O desinstalador registrado não teve assinatura Microsoft válida confirmada. Nenhum executável foi iniciado.");
        var outcome = await run(plan);
        OneDriveSnapshot? after = null;
        for (int i = 0; i < Math.Max(1, verifyAttempts); i++) {
            after = await detect();
            if (after.Errors.Length > 0 || !after.Present) break;
            if (i + 1 < verifyAttempts) await Task.Delay(500, ct);
        }
        string details = "Desinstalador: " + plan.Executable + ". Retorno: " + outcome.Code + ".";
        if (!string.IsNullOrWhiteSpace(outcome.Error)) details += "\n" + outcome.Error.Trim();
        if (!string.IsNullOrWhiteSpace(outcome.Output)) details += "\n" + outcome.Output.Trim();
        if (after is not null && after.Errors.Length == 0 && !after.Present)
            return new("Desinstalar o cliente OneDrive", "OneDrive", plan.Scope, "Removido", details + "\nAusência confirmada em nova consulta de registro e cliente. O app não apagou arquivos pessoais.");
        return new("Desinstalar o cliente OneDrive", "OneDrive", plan.Scope, "Erro", details + "\nA remoção não foi confirmada. " + (after?.Errors.Length > 0 ? string.Join("; ", after.Errors) : "A instalação ou o cliente ainda foi detectado.") + " Confira Microsoft OneDrive em Configurações > Aplicativos. Não é necessário apagar a pasta OneDrive.");
    }
}
