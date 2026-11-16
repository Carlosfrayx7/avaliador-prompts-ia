using System.Text.Json;
using PromptEval.Core.Data;
using PromptEval.Core.Models;

namespace PromptEval.Core.Runs;

/// <summary>Monta o <c>data.json</c> do painel estático a partir das execuções versionadas.</summary>
public static class SiteData
{
    public static string Build(
        IReadOnlyList<RunResult> runs, IReadOnlyList<TicketCase> cases, IReadOnlyList<PromptVersion> prompts,
        JudgeCalibrationResult? calibration, DateTimeOffset generatedAt)
    {
        var data = new
        {
            geradoEm = generatedAt,
            execucoes = runs.Select(r => new
            {
                chave = r.Key,
                prompt = r.PromptId,
                promptNome = r.PromptName,
                modelo = ClaudeModels.ShortName(r.Model),
                modeloId = r.Model,
                juiz = r.JudgeModel,
                executadoEm = r.RanAt,
                resumo = RunSummary.Of(r),
                casos = r.Cases.Select(c => new
                {
                    id = c.Id,
                    nota = Math.Round(c.Score, 4),
                    saida = c.Output,
                    erro = c.Error,
                    avaliacoes = c.Grades,
                    juizNotas = c.JudgeScores,
                    custoUsd = c.CostUsd,
                    latenciaMs = c.LatencyMs,
                }),
            }),
            casos = cases,
            prompts = prompts.Select(p => new { id = p.Id, nome = p.Name, descricao = p.Description, texto = p.SystemPrompt }),
            calibracao = calibration,
        };
        return JsonSerializer.Serialize(data, RunResult.JsonOptions);
    }
}
