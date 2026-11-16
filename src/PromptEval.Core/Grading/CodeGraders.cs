using System.Text.RegularExpressions;
using PromptEval.Core.Data;
using PromptEval.Core.Triage;

namespace PromptEval.Core.Grading;

/// <summary>Avaliadores determinísticos: rápidos, grátis e sem variação entre execuções.</summary>
public static partial class CodeGraders
{
    public const int MinWords = 20;
    public const int MaxWords = 180;

    public static IReadOnlyList<Grade> Grade(TicketCase ticket, TriageOutput? output)
    {
        if (output is null)
        {
            const string noOutput = "sem saída válida do modelo";
            return
            [
                new(GraderNames.Category, false, noOutput),
                new(GraderNames.Urgency, false, noOutput),
                new(GraderNames.Format, false, noOutput),
                new(GraderNames.Policy, false, noOutput),
            ];
        }

        return
        [
            Exact(GraderNames.Category, ticket.Category, output.Category),
            Exact(GraderNames.Urgency, ticket.Urgency, output.Urgency),
            Format(output.Reply),
            Policy(output.Reply),
        ];
    }

    private static Grade Exact(string grader, string expected, string actual) =>
        string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)
            ? new(grader, true, actual)
            : new(grader, false, $"esperado {expected}, veio {actual}");

    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static Grade Format(string reply)
    {
        var words = CountWords(reply);
        return words is >= MinWords and <= MaxWords
            ? new(GraderNames.Format, true, $"{words} palavras")
            : new(GraderNames.Format, false, $"{words} palavras (esperado de {MinWords} a {MaxWords})");
    }

    /// <summary>
    /// Regras da política da loja que dá para checar com texto: nunca pedir senha nem dados de cartão,
    /// nunca garantir reembolso antes da análise e nunca oferecer compensação que a política não prevê.
    /// O que depende de interpretação (prazo inventado, tom) fica com o juiz.
    /// </summary>
    public static Grade Policy(string reply)
    {
        var violations = ForbiddenPatterns
            .Where(p => p.Pattern.IsMatch(reply))
            .Select(p => p.Description)
            .ToList();
        return violations.Count == 0
            ? new(GraderNames.Policy, true, "sem violações")
            : new(GraderNames.Policy, false, string.Join("; ", violations));
    }

    private static readonly (Regex Pattern, string Description)[] ForbiddenPatterns =
    [
        (AskPassword(), "pede a senha do cliente"),
        (AskCard(), "pede dados do cartão"),
        (GuaranteedRefund(), "garante reembolso antes da análise"),
        (Coupon(), "oferece cupom ou desconto que a política não prevê"),
    ];

    [GeneratedRegex(@"\b(informe|envie|mande|passe|confirme|digite|nos diga)\b[^.?!]{0,40}\bsenha\b", RegexOptions.IgnoreCase)]
    private static partial Regex AskPassword();

    [GeneratedRegex(@"\b(informe|envie|mande|passe|confirme|digite|nos diga)\b[^.?!]{0,40}\b(n[úu]mero do cart[ãa]o|cvv|c[óo]digo de seguran[çc]a|dados do cart[ãa]o)", RegexOptions.IgnoreCase)]
    private static partial Regex AskCard();

    [GeneratedRegex(@"\b(reembolso|estorno|dinheiro de volta)\b[^.?!]{0,30}\b(garantid[oa]|j[áa] (foi |est[áa] )?aprovad[oa])|\bgarant(o|imos)\b[^.?!]{0,30}\b(reembolso|estorno)", RegexOptions.IgnoreCase)]
    private static partial Regex GuaranteedRefund();

    [GeneratedRegex(@"\b(cupom|vale[- ]compra|\d+ ?% de desconto)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Coupon();
}
