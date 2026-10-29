using System.Text.RegularExpressions;
namespace Diagnostico;

public enum ActivationMethod { WindowsHwid, OfficeOhook, RemoveOfficeOhook }
public record LicenseProduct(string Name, string Application, string Family, int Status);
public record ActivationSnapshot(string Windows, string Edition, int Build, bool ClientWindows, bool? WindowsLicensed, bool OfficeInstalled, string[] OpenOfficeApps, LicenseProduct[] Products, string[] Errors);
public record ActivationRun(int ExitCode, string Output, string Error);
public static class ActivationFlow
{
    public const string EngineVersion = "3.12";
    public const string EngineHash = "850f979665fb93999acae93f4790c1ff8ed2041532060b7966a121c2d29a0bfa";
    public static string Arguments(ActivationMethod method) => method switch {
        ActivationMethod.WindowsHwid => "/HWID /HWID-NoEditionChange -el",
        ActivationMethod.OfficeOhook => "/Ohook -el",
        ActivationMethod.RemoveOfficeOhook => "/Ohook-Uninstall -el",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
    public static string Name(ActivationMethod method) => method switch {
        ActivationMethod.WindowsHwid => "Windows · HWID (preservar edição)",
        ActivationMethod.OfficeOhook => "Office · Ohook",
        ActivationMethod.RemoveOfficeOhook => "Office · remover Ohook",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
    public static string Clean(string text)
    {
        text = Regex.Replace(text, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
        text = Regex.Replace(text, @"(?i)\b[A-Z0-9]{5}(?:-[A-Z0-9]{5}){4}\b", "[chave de produto ocultada]");
        return string.Concat(text.Where(c => !char.IsControl(c) || c is '\r' or '\n' or '\t'));
    }
    public static void Validate(ActivationMethod[] methods, ActivationSnapshot state)
    {
        if (methods.Length == 0 || methods.Distinct().Count() != methods.Length) throw new ArgumentException("Seleção vazia ou duplicada.");
        foreach (var method in methods) _ = Arguments(method);
        if (methods.Contains(ActivationMethod.RemoveOfficeOhook) && methods.Length != 1) throw new ArgumentException("Remoção de Ohook é uma operação separada.");
        if (methods.Contains(ActivationMethod.WindowsHwid) && (!state.ClientWindows || state.Build < 10240)) throw new InvalidOperationException("O método Windows desta interface exige Windows 10/11 cliente. A edição não será trocada automaticamente.");
        if (methods.Any(m => m is ActivationMethod.OfficeOhook or ActivationMethod.RemoveOfficeOhook)) {
            if (!state.OfficeInstalled) throw new InvalidOperationException("Office desktop não encontrado. Hub Microsoft 365 e novo Outlook não são uma instalação da suíte.");
            if (state.OpenOfficeApps.Length > 0) throw new InvalidOperationException("Salve os documentos e feche os aplicativos antes de continuar: " + string.Join(", ", state.OpenOfficeApps));
        }
    }
    public static async Task<TestResult> Execute(ActivationMethod[] methods, Func<Task<ActivationSnapshot>> detect,
        Func<ActivationMethod, Task<ActivationRun>> run, IProgress<string>? progress = null)
    {
        var before = await detect(); Validate(methods, before);
        var rows = new List<OperationRow>();
        foreach (var method in methods) {
            progress?.Report("Iniciando " + Name(method));
            if (method == ActivationMethod.WindowsHwid && before.WindowsLicensed == true) {
                rows.Add(new(Name(method), before.Edition, "Computador", "Já licenciado", "Consulta local identificou o Windows atual licenciado; nenhuma ativação executada.")); continue;
            }
            // Recheck Office processes just before handing control to the engine.
            var current = await detect(); Validate([method], current);
            ActivationRun result;
            try { result = await run(method); }
            catch (Exception e) { rows.Add(new(Name(method), "MAS " + EngineVersion, "Computador", "Erro", Clean(e.Message))); continue; }
            string output = Clean(result.Output + "\n" + result.Error);
            var after = await detect();
            string state, details;
            if (method == ActivationMethod.WindowsHwid) {
                bool correctEdition = after.Edition == before.Edition;
                state = result.ExitCode == 0 && after.WindowsLicensed == true && correctEdition ? "Licenciado" : "Erro";
                details = correctEdition ? "Consulta após execução: Windows atual licenciado=" + after.WindowsLicensed + "." : "A edição detectada mudou. Confira manualmente antes de continuar.";
            } else {
                bool reported = method == ActivationMethod.OfficeOhook
                    ? output.Contains("Office is permanently activated.", StringComparison.OrdinalIgnoreCase)
                    : output.Contains("Successfully uninstalled Ohook activation.", StringComparison.OrdinalIgnoreCase);
                state = result.ExitCode == 0 && reported ? "Informado pelo motor" : "Erro";
                details = method == ActivationMethod.OfficeOhook
                    ? "Resultado do MAS; Ohook não equivale ao estado WMI de uma licença Office. Abra Word/Excel para conferir o funcionamento."
                    : "Resultado do MAS; remoção de Ohook não restaura todas as alterações de licenciamento anteriores.";
            }
            rows.Add(new(Name(method), "MAS " + EngineVersion, "Computador", state, "Retorno=" + result.ExitCode + ". " + details + "\n" + output + (after.Errors.Length > 0 ? "\nPendências da consulta: " + string.Join("; ", after.Errors) : "")));
        }
        return new("Ativação · RBT", rows.Any(r => r.Failed) ? "Concluído com falhas; conferir detalhes" : rows.Any(r => r.Estado == "Informado pelo motor") ? "Resultado do motor; confirmação Office pendente" : "Windows licenciado na consulta local", new() { ["Etapas"] = rows.ToArray(), ["Motor"] = "Microsoft Activation Scripts " + EngineVersion + " · massgravel e colaboradores · GPL-3.0", ["SHA256 do motor"] = EngineHash, ["Estado antes"] = before, ["Observação"] = "Estado local/resultado técnico não comprova aquisição de licença. Nenhum resultado é inferido apenas pelo código de saída." }, DateTimeOffset.Now);
    }
}
