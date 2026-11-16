using System.Diagnostics;
using PromptEval.Core.Data;
using PromptEval.Core.Grading;
using PromptEval.Core.Models;
using PromptEval.Core.Triage;

namespace PromptEval.Core.Runs;

/// <summary>Executa uma versão de prompt num modelo sobre todos os casos e avalia cada resposta.</summary>
public sealed class EvalRunner(IMessagesGateway gateway, JudgeGrader judge, TimeProvider clock, int parallelism = 4)
{
    public async Task<RunResult> RunAsync(
        PromptVersion prompt, string model, IReadOnlyList<TicketCase> cases,
        IProgress<int>? progress, CancellationToken ct)
    {
        // O primeiro caso grava o cache do prompt de sistema; os demais esperam por ele para aproveitar a leitura.
        var results = new CaseResult[cases.Count];
        var done = 0;
        if (cases.Count > 0)
        {
            results[0] = await RunCaseAsync(prompt, model, cases[0], ct);
            progress?.Report(Interlocked.Increment(ref done));
        }

        await Parallel.ForEachAsync(
            Enumerable.Range(1, Math.Max(0, cases.Count - 1)),
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            async (i, token) =>
            {
                results[i] = await RunCaseAsync(prompt, model, cases[i], token);
                progress?.Report(Interlocked.Increment(ref done));
            });

        return new RunResult(prompt.Id, prompt.Name, prompt.Description, model, judge.Model, clock.GetLocalNow(), results);
    }

    public async Task<CaseResult> RunCaseAsync(PromptVersion prompt, string model, TicketCase ticket, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        TriageOutput? output = null;
        string? error = null;
        var usage = TokenUsage.Zero;

        try
        {
            var response = await gateway.SendAsync(
                new GatewayRequest(model, prompt.SystemPrompt, TriageTask.UserMessage(ticket), TriageTask.Schema,
                    TriageTask.MaxTokens, CacheSystemPrompt: true), ct);
            usage = response.Usage;
            if (response.StopReason == "end_turn")
            {
                output = TriageTask.TryParse(response.Text);
                if (output is null) error = "JSON inválido na resposta";
            }
            else
            {
                error = response.StopReason == "refusal"
                    ? $"recusa do modelo ({response.RefusalCategory ?? "sem categoria"})"
                    : $"stop_reason {response.StopReason}";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.GetType().Name + ": " + ex.Message;
        }

        var latency = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var grades = CodeGraders.Grade(ticket, output).ToList();

        JudgeVerdict? verdict = null;
        if (output is not null)
        {
            verdict = await judge.JudgeAsync(ticket, output.Reply, ct);
            grades.Add(verdict.ToGrade());
        }
        else
        {
            grades.Add(new Grade(GraderNames.Judge, false, "sem resposta para julgar"));
        }

        return new CaseResult(
            ticket.Id, ticket.Tags, output, error, grades, usage,
            ModelPricing.EstimateUsd(model, usage), latency,
            verdict?.Scores, verdict?.CostUsd ?? 0);
    }
}
