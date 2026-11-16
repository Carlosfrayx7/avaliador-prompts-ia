using PromptEval.Core.Data;
using PromptEval.Core.Grading;
using PromptEval.Core.Models;
using PromptEval.Core.Runs;

namespace PromptEval.Core.Tests;

public class EvalRunnerTests
{
    private static readonly PromptVersion Prompt = new("v9", "v9", "teste", "PROMPT-SISTEMA");

    private static EvalRunner Runner(FakeGateway gateway) =>
        new(gateway, new JudgeGrader(gateway, "politica"), TimeProvider.System, parallelism: 2);

    [Fact]
    public async Task Runs_every_case_and_grades_with_code_and_judge()
    {
        var gateway = new FakeGateway(r => FakeGateway.IsJudge(r)
            ? FakeGateway.Ok(FakeGateway.GoodJudge)
            : FakeGateway.Ok($$"""{"categoria":"entrega","urgencia":"media","resposta":"{{Build.Reply()}}"}"""));
        var cases = Enumerable.Range(1, 5).Select(i => Build.Case($"c{i}")).ToList();

        var run = await Runner(gateway).RunAsync(Prompt, ClaudeModels.Haiku, cases, null, CancellationToken.None);

        Assert.Equal(cases.Select(c => c.Id), run.Cases.Select(c => c.Id));
        Assert.All(run.Cases, c => Assert.Equal(1.0, c.Score));
        Assert.All(run.Cases, c => Assert.Equal(GraderNames.All, c.Grades.Select(g => g.Grader)));
        Assert.Equal("v9__haiku", run.Key);

        var triage = gateway.Requests.Where(r => !FakeGateway.IsJudge(r)).ToList();
        Assert.Equal(5, triage.Count);
        Assert.All(triage, r =>
        {
            Assert.Equal("PROMPT-SISTEMA", r.SystemPrompt);
            Assert.True(r.CacheSystemPrompt);
            Assert.StartsWith("<chamado>", r.UserText);
        });
    }

    [Fact]
    public async Task Refusal_is_recorded_as_error_and_not_judged()
    {
        var gateway = new FakeGateway(r => FakeGateway.IsJudge(r)
            ? throw new InvalidOperationException("juiz não deveria ser chamado")
            : new GatewayResponse("refusal", null, new TokenUsage(50, 0), "cyber"));

        var result = await Runner(gateway).RunCaseAsync(Prompt, ClaudeModels.Haiku, Build.Case(), CancellationToken.None);

        Assert.Contains("recusa", result.Error);
        Assert.Equal(0, result.Score);
        Assert.Equal(0, result.JudgeCostUsd);
    }

    [Fact]
    public async Task Invalid_json_counts_as_error()
    {
        var gateway = new FakeGateway(_ => FakeGateway.Ok("{\"categoria\":"));
        var result = await Runner(gateway).RunCaseAsync(Prompt, ClaudeModels.Haiku, Build.Case(), CancellationToken.None);
        Assert.Equal("JSON inválido na resposta", result.Error);
    }

    [Fact]
    public async Task Cost_includes_cache_reads_and_writes()
    {
        var gateway = new FakeGateway(r => FakeGateway.IsJudge(r)
            ? FakeGateway.Ok(FakeGateway.BadJudge)
            : new GatewayResponse("end_turn", $$"""{"categoria":"entrega","urgencia":"media","resposta":"{{Build.Reply()}}"}""",
                new TokenUsage(Input: 100, Output: 200, CacheRead: 1_000_000, CacheWrite: 0), null));

        var result = await Runner(gateway).RunCaseAsync(Prompt, ClaudeModels.Haiku, Build.Case(), CancellationToken.None);

        // 100 × 0,10 + 1.000.000 × 0,01 + 200 × 0,50, por milhão.
        Assert.Equal(0.0101100m, result.CostUsd);
        Assert.False(result.Passed(GraderNames.Judge));
        Assert.Equal(0.8, result.Score, 3);
    }
}
