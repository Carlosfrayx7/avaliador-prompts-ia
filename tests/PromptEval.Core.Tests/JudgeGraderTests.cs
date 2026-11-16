using PromptEval.Core.Grading;
using PromptEval.Core.Models;

namespace PromptEval.Core.Tests;

public class JudgeGraderTests
{
    [Theory]
    [InlineData(4, 4, 4, true)]
    [InlineData(5, 5, 3, false)]
    [InlineData(3, 5, 5, false)]
    public void Approval_requires_every_score_at_least_four(int coverage, int tone, int faithfulness, bool expected)
    {
        Assert.Equal(expected, new JudgeScores(coverage, tone, faithfulness, "").Approved);
    }

    [Theory]
    [InlineData("""{"justificativa":"x","cobertura":0,"tom":5,"fidelidade":5}""")]
    [InlineData("não é json")]
    [InlineData("")]
    public void TryParse_rejects_invalid_output(string text)
    {
        Assert.Null(JudgeGrader.TryParse(text));
    }

    [Fact]
    public async Task Judge_sends_policy_points_and_reply_with_cached_system_prompt()
    {
        var gateway = new FakeGateway(_ => FakeGateway.Ok(FakeGateway.GoodJudge));
        var judge = new JudgeGrader(gateway, "POLITICA-TESTE");

        var verdict = await judge.JudgeAsync(Build.Case(), "RASCUNHO-TESTE", CancellationToken.None);

        Assert.True(verdict.ToGrade().Passed);
        var request = Assert.Single(gateway.Requests);
        Assert.Equal(ClaudeModels.Opus, request.Model);
        Assert.True(request.CacheSystemPrompt);
        Assert.Contains("POLITICA-TESTE", request.SystemPrompt);
        Assert.Contains("RASCUNHO-TESTE", request.UserText);
        Assert.Contains("- ponto", request.UserText);
        Assert.True(verdict.CostUsd > 0);
    }

    [Fact]
    public async Task Refusal_or_bad_json_become_failed_grade_with_reason()
    {
        var refusal = new JudgeGrader(new FakeGateway(_ => new GatewayResponse("refusal", null, new TokenUsage(10, 0), "cyber")), "p");
        var verdict = await refusal.JudgeAsync(Build.Case(), "r", CancellationToken.None);
        Assert.False(verdict.ToGrade().Passed);
        Assert.Contains("refusal", verdict.Error);

        var broken = new JudgeGrader(new FakeGateway(_ => FakeGateway.Ok("{")), "p");
        Assert.Contains("inválido", (await broken.JudgeAsync(Build.Case(), "r", CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Gateway_exception_is_captured_not_thrown()
    {
        var judge = new JudgeGrader(new FakeGateway(_ => throw new HttpRequestException("rede caiu")), "p");
        var verdict = await judge.JudgeAsync(Build.Case(), "r", CancellationToken.None);
        Assert.Null(verdict.Scores);
        Assert.Contains("rede caiu", verdict.Error);
    }
}
