using System.Text.Json.Serialization;
using PromptEval.Core.Data;
using PromptEval.Core.Grading;

namespace PromptEval.Core.Runs;

public sealed record CalibrationItemResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("caso")] string CaseId,
    [property: JsonPropertyName("esperado")] bool Expected,
    [property: JsonPropertyName("juiz")] bool? Actual,
    [property: JsonPropertyName("motivo")] string Reason,
    [property: JsonPropertyName("notas")] JudgeScores? Scores,
    [property: JsonPropertyName("erro")] string? Error)
{
    [JsonIgnore]
    public bool Agrees => Actual == Expected;
}

/// <summary>
/// Mede se o juiz concorda com vereditos humanos. Um juiz que aprova tudo daria nota alta para qualquer prompt,
/// então a métrica que importa é quantas respostas ruins ele reprova (e quantas boas ele não reprova à toa).
/// </summary>
public sealed record JudgeCalibrationResult(
    [property: JsonPropertyName("juiz")] string JudgeModel,
    [property: JsonPropertyName("executadoEm")] DateTimeOffset RanAt,
    [property: JsonPropertyName("itens")] IReadOnlyList<CalibrationItemResult> Items,
    [property: JsonPropertyName("custoUsd")] decimal CostUsd)
{
    [JsonPropertyName("concordancia")]
    public double Agreement => Items.Count == 0 ? 0 : Items.Count(i => i.Agrees) / (double)Items.Count;

    [JsonPropertyName("ruinsReprovadas")]
    public double BadCaught => Rate(Items.Where(i => !i.Expected), i => i.Actual == false);

    [JsonPropertyName("boasAprovadas")]
    public double GoodKept => Rate(Items.Where(i => i.Expected), i => i.Actual == true);

    private static double Rate(IEnumerable<CalibrationItemResult> items, Func<CalibrationItemResult, bool> ok)
    {
        var list = items.ToList();
        return list.Count == 0 ? 0 : list.Count(ok) / (double)list.Count;
    }

    public static async Task<JudgeCalibrationResult> RunAsync(
        JudgeGrader judge, IReadOnlyList<JudgeCalibrationItem> items, IReadOnlyList<TicketCase> cases,
        TimeProvider clock, CancellationToken ct)
    {
        var byId = cases.ToDictionary(c => c.Id);
        var results = new CalibrationItemResult[items.Count];
        decimal cost = 0;
        var gate = new object();

        await Parallel.ForEachAsync(Enumerable.Range(0, items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (i, token) =>
            {
                var item = items[i];
                if (!byId.TryGetValue(item.CaseId, out var ticket))
                    throw new InvalidDataException($"{item.Id}: caso '{item.CaseId}' não existe na base.");
                var verdict = await judge.JudgeAsync(ticket, item.Reply, token);
                results[i] = new CalibrationItemResult(item.Id, item.CaseId, item.ExpectedApproved,
                    verdict.Scores?.Approved, item.Reason, verdict.Scores, verdict.Error);
                lock (gate) cost += verdict.CostUsd;
            });

        return new JudgeCalibrationResult(judge.Model, clock.GetLocalNow(), results, cost);
    }
}
