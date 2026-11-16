using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptEval.Core.Data;

/// <summary>Um chamado fictício com gabarito.</summary>
public sealed record TicketCase(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("texto")] string Text,
    [property: JsonPropertyName("categoria")] string Category,
    [property: JsonPropertyName("urgencia")] string Urgency,
    [property: JsonPropertyName("pontos")] IReadOnlyList<string> Points,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags);

/// <summary>Resposta escrita à mão com veredito conhecido, usada para medir se o juiz concorda com uma pessoa.</summary>
public sealed record JudgeCalibrationItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("caso")] string CaseId,
    [property: JsonPropertyName("resposta")] string Reply,
    [property: JsonPropertyName("aprovado")] bool ExpectedApproved,
    [property: JsonPropertyName("motivo")] string Reason);

public static class Taxonomy
{
    public static readonly IReadOnlyList<string> Categories =
        ["entrega", "pagamento", "troca_devolucao", "produto_defeituoso", "conta_acesso", "outro"];

    public static readonly IReadOnlyList<string> Urgencies = ["baixa", "media", "alta"];
}

public static class JsonLines
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = false };

    public static IReadOnlyList<T> Parse<T>(string content, string source)
    {
        var items = new List<T>();
        var lineNumber = 0;
        foreach (var raw in content.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            try
            {
                items.Add(JsonSerializer.Deserialize<T>(line, Options)
                          ?? throw new InvalidDataException("linha nula"));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{source}:{lineNumber}: JSON inválido ({ex.Message})", ex);
            }
        }
        return items;
    }

    public static IReadOnlyList<TicketCase> LoadCases(string path)
    {
        var cases = Parse<TicketCase>(File.ReadAllText(path), Path.GetFileName(path));
        Validate(cases);
        return cases;
    }

    public static void Validate(IReadOnlyList<TicketCase> cases)
    {
        var duplicate = cases.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Id repetido: {duplicate.Key}");
        foreach (var c in cases)
        {
            if (!Taxonomy.Categories.Contains(c.Category))
                throw new InvalidDataException($"{c.Id}: categoria desconhecida '{c.Category}'");
            if (!Taxonomy.Urgencies.Contains(c.Urgency))
                throw new InvalidDataException($"{c.Id}: urgência desconhecida '{c.Urgency}'");
            if (c.Points.Count == 0) throw new InvalidDataException($"{c.Id}: sem pontos esperados na resposta");
        }
    }
}
