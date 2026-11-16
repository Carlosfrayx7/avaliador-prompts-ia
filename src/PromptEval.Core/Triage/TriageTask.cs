using System.Text.Json;
using System.Text.Json.Serialization;
using PromptEval.Core.Data;

namespace PromptEval.Core.Triage;

public sealed record TriageOutput(
    [property: JsonPropertyName("categoria")] string Category,
    [property: JsonPropertyName("urgencia")] string Urgency,
    [property: JsonPropertyName("resposta")] string Reply);

public static class TriageTask
{
    public const int MaxTokens = 8_000;

    public static readonly JsonElement Schema = JsonDocument.Parse($$"""
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["categoria", "urgencia", "resposta"],
      "properties": {
        "categoria": { "type": "string", "enum": {{JsonSerializer.Serialize(Taxonomy.Categories)}} },
        "urgencia": { "type": "string", "enum": {{JsonSerializer.Serialize(Taxonomy.Urgencies)}} },
        "resposta": { "type": "string" }
      }
    }
    """).RootElement;

    /// <summary>
    /// O chamado vai delimitado por tags: o texto é do cliente e pode trazer instruções
    /// ("ignore as regras..."), que devem ser tratadas como conteúdo, não como comando.
    /// </summary>
    public static string UserMessage(TicketCase ticket) =>
        $"<chamado>\n{ticket.Text}\n</chamado>";

    public static TriageOutput? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var output = JsonSerializer.Deserialize<TriageOutput>(text);
            return output is { Category: not null, Urgency: not null, Reply: not null } ? output : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
