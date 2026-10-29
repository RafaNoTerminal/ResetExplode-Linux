using System.Text.Json;

namespace Diagnostico;

public record OperationRow(string Acao, string Item, string Escopo, string Estado, string Detalhes)
{
    [System.ComponentModel.Browsable(false), System.Text.Json.Serialization.JsonIgnore]
    public bool Failed => Estado.Equals("Erro", StringComparison.OrdinalIgnoreCase) || Estado.StartsWith("Falha", StringComparison.OrdinalIgnoreCase);
}

public static class OperationResults
{
    public static List<OperationRow> ForAction(SetupAction action, IEnumerable<OperationRow> inventory) => inventory
        .Where(row => action.AppPattern is not null && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(action.AppPattern, row.Item.Split('_')[0], ignoreCase: true)).ToList();
    public static bool StillPresent(SetupAction action, IEnumerable<OperationRow> inventory)
    {
        var rows = ForAction(action, inventory);
        // An unsuccessful/missing query never proves the app was removed.
        return rows.Count == 0 || rows.Any(row => row.Estado is not ("Ausente" or "Removido"));
    }
    public static List<OperationRow> Read(TestResult result)
    {
        if (result.Dados.TryGetValue("Etapas", out var steps))
            return JsonSerializer.Deserialize<List<OperationRow>>(JsonSerializer.Serialize(steps)) ?? [];
        // Older reports stored Appx errors inside a nested array and registry errors as strings.
        var rows = new List<OperationRow>();
        foreach (var field in result.Dados) {
            string? value = field.Value is string text ? text : field.Value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : null;
            if (field.Key == "Pacotes Appx" && value is not null) rows.Add(new("Remover apps", "Execução PowerShell", "Seleção de apps", "Erro", value));
            else if (field.Key == "Pacotes Appx") rows.AddRange(Packages(JsonSerializer.Serialize(field.Value)));
            else if (value is not null && value.StartsWith("Erro:", StringComparison.OrdinalIgnoreCase))
                rows.Add(new(field.Key, field.Key, "Ajuste", "Erro", value[5..].Trim()));
        }
        return rows;
    }
    public static List<OperationRow> Packages(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("O Windows não retornou uma lista de resultados dos apps.");
        return document.RootElement.EnumerateArray().Select(row => {
            string Value(string key) => row.TryGetProperty(key, out var value) ? value.ToString() : "";
            return new OperationRow(row.TryGetProperty("Acao", out var action) ? action.ToString() : "Remover app", Value("Pacote"), Value("Escopo"), Value("Estado"), Value("Detalhes"));
        }).ToList();
    }
    public static string Format(object? value) => value is string text ? text : JsonSerializer.Serialize(value, Reports.JsonOptions);
    public static string Summary(TestResult result)
    {
        var rows = Read(result);
        return rows.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, rows.Select(r => $"{r.Estado} · {r.Acao} · {r.Item}{Environment.NewLine}{r.Escopo}: {r.Detalhes}"))
            : string.Join(Environment.NewLine, result.Dados.Select(p => p.Key + ": " + Format(p.Value)));
    }
}
