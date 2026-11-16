namespace PromptEval.Core.Runs;

public sealed record CaseChange(string Id, double Before, double After, IReadOnlyList<string> Gained, IReadOnlyList<string> Lost);

/// <summary>Compara duas execuções caso a caso: o que melhorou, o que piorou e o que sumiu.</summary>
public sealed record RunComparison(
    RunResult Baseline,
    RunResult Candidate,
    IReadOnlyList<CaseChange> Improved,
    IReadOnlyList<CaseChange> Regressed,
    IReadOnlyList<string> MissingInCandidate)
{
    public double ScoreDelta => RunSummary.Of(Candidate).Score - RunSummary.Of(Baseline).Score;

    public static RunComparison Of(RunResult baseline, RunResult candidate)
    {
        var after = candidate.Cases.ToDictionary(c => c.Id);
        var improved = new List<CaseChange>();
        var regressed = new List<CaseChange>();
        var missing = new List<string>();

        foreach (var before in baseline.Cases)
        {
            if (!after.TryGetValue(before.Id, out var now))
            {
                missing.Add(before.Id);
                continue;
            }

            var gained = now.Grades.Where(g => g.Passed && !before.Passed(g.Grader)).Select(g => g.Grader).ToList();
            var lost = before.Grades.Where(g => g.Passed && !now.Passed(g.Grader)).Select(g => g.Grader).ToList();
            if (gained.Count == 0 && lost.Count == 0) continue;

            var change = new CaseChange(before.Id, before.Score, now.Score, gained, lost);
            if (now.Score > before.Score || (now.Score == before.Score && lost.Count == 0)) improved.Add(change);
            else regressed.Add(change);
        }

        return new RunComparison(baseline, candidate, improved, regressed, missing);
    }
}

public sealed record GateResult(bool Passed, IReadOnlyList<string> Reasons);

/// <summary>
/// Portão de regressão: uma versão nova de prompt só passa se a nota geral não cair mais que o limite
/// e se nenhum caso de segurança (tag <c>injecao</c>) que passava na política e no juiz deixar de passar.
/// </summary>
public static class RegressionGate
{
    public const string SafetyTag = "injecao";

    public static GateResult Check(RunResult baseline, RunResult candidate, double maxDrop)
    {
        var comparison = RunComparison.Of(baseline, candidate);
        var reasons = new List<string>();

        if (comparison.MissingInCandidate.Count > 0)
            reasons.Add($"casos ausentes na versão nova: {string.Join(", ", comparison.MissingInCandidate)}");

        if (comparison.ScoreDelta < -maxDrop)
            reasons.Add($"nota caiu {-comparison.ScoreDelta:P1} (limite {maxDrop:P1})");

        var safetyIds = baseline.Cases.Where(c => c.Tags.Contains(SafetyTag)).Select(c => c.Id).ToHashSet();
        foreach (var change in comparison.Regressed.Where(c => safetyIds.Contains(c.Id)))
        {
            var critical = change.Lost.Where(g => g is Grading.GraderNames.Policy or Grading.GraderNames.Judge).ToList();
            if (critical.Count > 0)
                reasons.Add($"caso de segurança {change.Id} deixou de passar em: {string.Join(", ", critical)}");
        }

        return new GateResult(reasons.Count == 0, reasons);
    }
}
