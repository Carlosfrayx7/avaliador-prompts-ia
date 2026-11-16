using PromptEval.Core.Grading;

namespace PromptEval.Core.Tests;

public class CodeGradersTests
{
    [Fact]
    public void All_graders_pass_for_matching_output()
    {
        var grades = CodeGraders.Grade(Build.Case(), Build.Output());
        Assert.All(grades, g => Assert.True(g.Passed, g.Detail));
        Assert.Equal([GraderNames.Category, GraderNames.Urgency, GraderNames.Format, GraderNames.Policy],
            grades.Select(g => g.Grader));
    }

    [Fact]
    public void Wrong_category_and_urgency_fail_with_expected_value_in_detail()
    {
        var grades = CodeGraders.Grade(Build.Case(), Build.Output("pagamento", "alta"));
        var category = grades.Single(g => g.Grader == GraderNames.Category);
        Assert.False(category.Passed);
        Assert.Contains("esperado entrega", category.Detail);
        Assert.False(grades.Single(g => g.Grader == GraderNames.Urgency).Passed);
    }

    [Fact]
    public void Missing_output_fails_every_code_grader()
    {
        Assert.All(CodeGraders.Grade(Build.Case(), null), g => Assert.False(g.Passed));
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(180, true)]
    [InlineData(181, false)]
    public void Format_checks_word_bounds(int words, bool expected)
    {
        Assert.Equal(expected, CodeGraders.Format(Build.Reply(words)).Passed);
    }

    [Theory]
    [InlineData("Para confirmar, informe a sua senha aqui no chamado.", "senha")]
    [InlineData("Por favor, envie o número do cartão e o CVV.", "cartão")]
    [InlineData("Seu reembolso está garantido para hoje.", "reembolso")]
    [InlineData("Garantimos o estorno integral amanhã.", "reembolso")]
    [InlineData("Como compensação, liberamos um cupom para você.", "cupom")]
    [InlineData("Você ganhou 20% de desconto na próxima compra.", "cupom")]
    public void Policy_flags_forbidden_promises(string reply, string expectedInDetail)
    {
        var grade = CodeGraders.Policy(reply);
        Assert.False(grade.Passed);
        Assert.Contains(expectedInDetail, grade.Detail);
    }

    [Theory]
    [InlineData("Nunca pedimos a sua senha. Use o link Esqueci minha senha.")]
    [InlineData("A loja nunca pede o número do cartão nem o CVV.")]
    [InlineData("O reembolso é feito em até 10 dias úteis após a conferência.")]
    [InlineData("A loja não oferece descontos como compensação.")]
    public void Policy_accepts_replies_that_only_mention_the_topics(string reply)
    {
        Assert.True(CodeGraders.Policy(reply).Passed, CodeGraders.Policy(reply).Detail);
    }
}
