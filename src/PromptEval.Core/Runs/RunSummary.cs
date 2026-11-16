using System.Text.Json.Serialization;
using PromptEval.Core.Grading;

namespace PromptEval.Core.Runs;

public sealed record RunSummary(
    [property: JsonPropertyName("casos")] int Cases,
    [property: JsonPropertyName("nota")] double Score,
    [property: JsonPropertyName("casosAprovados")] double FullyPassedRate,
    [property: JsonPropertyName("porAvaliador")] IReadOnlyDictionary<string, double> ByGrader,
    [property: JsonPropertyName("porTag")] IReadOnlyDictionary<string, double> ByTag,
    [property: JsonPropertyName("erros")] int Errors,
    [property: JsonPropertyName("custoUsd")] decimal CostUsd,
    [property: JsonPropertyName("custoJuizUsd")] decimal JudgeCostUsd,
    [property: JsonPropertyName("latenciaMediaMs")] double MeanLatencyMs,
    [property: JsonPropertyName("latenciaP95Ms")] long P95LatencyMs,
    [property: JsonPropertyName("tokensEntrada")] long InputTokens,
    [property: JsonPropertyName("tokensSaida")] long OutputTokens,
    [property: JsonPropertyName("tokensCacheLidos")] long CacheReadTokens,
    [property: JsonPropertyName("tokensCacheGravados")] long CacheWriteTokens)
{
    public static RunSummary Of(RunResult run)
    {
        var cases = run.Cases;
        var n = cases.Count;

        var byGrader = GraderNames.All.ToDictionary(
            g => g,
            g => n == 0 ? 0 : cases.Count(c => c.Passed(g)) / (double)n);

        var byTag = cases
            .SelectMany(c => c.Tags.Select(t => (Tag: t, c.Score)))
            .GroupBy(x => x.Tag)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Average(x => x.Score));

        var latencies = cases.Where(c => c.Error is null).Select(c => c.LatencyMs).Order().ToList();

        return new RunSummary(
            n,
            n == 0 ? 0 : cases.Average(c => c.Score),
            n == 0 ? 0 : cases.Count(c => c.Score >= 1.0) / (double)n,
            byGrader,
            byTag,
            cases.Count(c => c.Error is not null),
            cases.Sum(c => c.CostUsd),
            cases.Sum(c => c.JudgeCostUsd),
            latencies.Count == 0 ? 0 : latencies.Average(),
            Percentile(latencies, 0.95),
            cases.Sum(c => c.Usage.Input),
            cases.Sum(c => c.Usage.Output),
            cases.Sum(c => c.Usage.CacheRead),
            cases.Sum(c => c.Usage.CacheWrite));
    }

    /// <summary>Percentil pelo método do posto mais próximo, sobre uma lista já ordenada.</summary>
    public static long Percentile(IReadOnlyList<long> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var rank = (int)Math.Ceiling(p * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}
