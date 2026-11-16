using System.Text.Json;
using System.Text.Json.Serialization;
using PromptEval.Core.Grading;
using PromptEval.Core.Models;
using PromptEval.Core.Triage;

namespace PromptEval.Core.Runs;

public sealed record CaseResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("saida")] TriageOutput? Output,
    [property: JsonPropertyName("erro")] string? Error,
    [property: JsonPropertyName("avaliacoes")] IReadOnlyList<Grade> Grades,
    [property: JsonPropertyName("uso")] TokenUsage Usage,
    [property: JsonPropertyName("custoUsd")] decimal CostUsd,
    [property: JsonPropertyName("latenciaMs")] long LatencyMs,
    [property: JsonPropertyName("juizNotas")] JudgeScores? JudgeScores,
    [property: JsonPropertyName("juizCustoUsd")] decimal JudgeCostUsd)
{
    /// <summary>Fração dos avaliadores que aprovaram o caso (0 a 1).</summary>
    [JsonIgnore]
    public double Score => Grades.Count == 0 ? 0 : Grades.Count(g => g.Passed) / (double)Grades.Count;

    public bool Passed(string grader) => Grades.Any(g => g.Grader == grader && g.Passed);
}

public sealed record RunResult(
    [property: JsonPropertyName("prompt")] string PromptId,
    [property: JsonPropertyName("promptNome")] string PromptName,
    [property: JsonPropertyName("promptDescricao")] string PromptDescription,
    [property: JsonPropertyName("modelo")] string Model,
    [property: JsonPropertyName("juiz")] string JudgeModel,
    [property: JsonPropertyName("executadoEm")] DateTimeOffset RanAt,
    [property: JsonPropertyName("casos")] IReadOnlyList<CaseResult> Cases)
{
    [JsonIgnore]
    public string Key => $"{PromptId}__{ClaudeModels.ShortName(Model)}";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static RunResult FromJson(string json) =>
        JsonSerializer.Deserialize<RunResult>(json, Options) ?? throw new InvalidDataException("Execução vazia.");

    public static RunResult Load(string path) => FromJson(File.ReadAllText(path));

    public static IReadOnlyList<RunResult> LoadAll(string runsDir) =>
        Directory.Exists(runsDir)
            ? Directory.GetFiles(runsDir, "*__*.json").Order(StringComparer.Ordinal).Select(Load).ToList()
            : [];

    public static JsonSerializerOptions JsonOptions => Options;
}
