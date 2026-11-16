using PromptEval.Core.Grading;
using PromptEval.Core.Runs;

namespace PromptEval.Core.Tests;

public class RunAnalysisTests
{
    private static readonly bool[] FailJudge = [true, true, true, true, false];
    private static readonly bool[] FailPolicyAndJudge = [true, true, true, false, false];

    [Fact]
    public void Summary_averages_scores_by_grader_and_tag()
    {
        var run = Build.Run("v1",
            Build.Result("a", Build.All(true), "simples"),
            Build.Result("b", FailJudge, "irritado"),
            Build.Result("c", Build.All(false), "irritado"));

        var s = RunSummary.Of(run);

        Assert.Equal((1.0 + 0.8 + 0.0) / 3, s.Score, 6);
        Assert.Equal(1 / 3.0, s.FullyPassedRate, 6);
        Assert.Equal(1 / 3.0, s.ByGrader[GraderNames.Judge], 6);
        Assert.Equal(2 / 3.0, s.ByGrader[GraderNames.Category], 6);
        Assert.Equal(0.4, s.ByTag["irritado"], 6);
        Assert.Equal(0.003m, s.CostUsd);
        Assert.Equal(0.006m, s.JudgeCostUsd);
    }

    [Theory]
    [InlineData(new long[] { }, 0)]
    [InlineData(new long[] { 5 }, 5)]
    [InlineData(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, 19)]
    public void Percentile_uses_nearest_rank(long[] sorted, long expected)
    {
        Assert.Equal(expected, RunSummary.Percentile(sorted, 0.95));
    }

    [Fact]
    public void Comparison_lists_improved_regressed_and_missing_cases()
    {
        var before = Build.Run("v1",
            Build.Result("a", FailJudge), Build.Result("b", Build.All(true)),
            Build.Result("c", Build.All(true)), Build.Result("d", Build.All(true)));
        var after = Build.Run("v2",
            Build.Result("a", Build.All(true)), Build.Result("b", FailJudge), Build.Result("c", Build.All(true)));

        var c = RunComparison.Of(before, after);

        Assert.Equal("a", Assert.Single(c.Improved).Id);
        var regressed = Assert.Single(c.Regressed);
        Assert.Equal("b", regressed.Id);
        Assert.Equal([GraderNames.Judge], regressed.Lost);
        Assert.Equal(["d"], c.MissingInCandidate);
    }

    [Fact]
    public void Gate_passes_when_score_holds()
    {
        var run = Build.Run("v1", Build.Result("a", Build.All(true)));
        Assert.True(RegressionGate.Check(run, run, 0.02).Passed);
    }

    [Fact]
    public void Gate_fails_on_score_drop_beyond_limit()
    {
        var before = Build.Run("v1", Build.Result("a", Build.All(true)), Build.Result("b", Build.All(true)));
        var after = Build.Run("v2", Build.Result("a", Build.All(true)), Build.Result("b", FailJudge));

        var result = RegressionGate.Check(before, after, maxDrop: 0.05);

        Assert.False(result.Passed);
        Assert.Contains(result.Reasons, r => r.Contains("nota caiu"));
        Assert.True(RegressionGate.Check(before, after, maxDrop: 0.15).Passed);
    }

    [Fact]
    public void Gate_fails_when_a_safety_case_loses_policy_or_judge_even_if_average_holds()
    {
        var before = Build.Run("v1",
            Build.Result("inj", Build.All(true), RegressionGate.SafetyTag), Build.Result("x", FailPolicyAndJudge));
        var after = Build.Run("v2",
            Build.Result("inj", FailPolicyAndJudge, RegressionGate.SafetyTag), Build.Result("x", Build.All(true)));

        var result = RegressionGate.Check(before, after, maxDrop: 0.5);

        Assert.False(result.Passed);
        Assert.Contains(result.Reasons, r => r.Contains("caso de segurança inj"));
    }

    [Fact]
    public void Run_json_round_trips()
    {
        var run = Build.Run("v1", Build.Result("a", FailJudge, "simples"));
        var back = RunResult.FromJson(run.ToJson());
        Assert.Equal(run.Key, back.Key);
        Assert.Equal(0.8, back.Cases[0].Score, 6);
        Assert.Contains("\"avaliacoes\"", run.ToJson());
    }
}
