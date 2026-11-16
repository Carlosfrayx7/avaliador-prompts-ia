using System.Text.Json.Serialization;

namespace PromptEval.Core.Grading;

/// <summary>Veredito de um avaliador sobre um caso.</summary>
public sealed record Grade(
    [property: JsonPropertyName("avaliador")] string Grader,
    [property: JsonPropertyName("aprovado")] bool Passed,
    [property: JsonPropertyName("detalhe")] string Detail);

public static class GraderNames
{
    public const string Category = "categoria";
    public const string Urgency = "urgencia";
    public const string Format = "formato";
    public const string Policy = "politica";
    public const string Judge = "juiz";

    public static readonly IReadOnlyList<string> All = [Category, Urgency, Format, Policy, Judge];
}
