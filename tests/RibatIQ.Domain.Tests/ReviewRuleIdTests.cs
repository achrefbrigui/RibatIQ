using RibatIQ.Domain.Rules;

namespace RibatIQ.Domain.Tests;

public sealed class ReviewRuleIdTests
{
    [Theory]
    [InlineData("SECURITY.EXPOSED_SECRET")]
    [InlineData("DOTNET.ASYNC.UNAWAITED")]
    [InlineData("DEPENDENCY.NUGET.VULNERABLE")]
    [InlineData("PERFORMANCE.NPLUSONE")]
    [InlineData("CSHARP.SYNTAX.ERROR")]
    [InlineData("A1.B2_C")]
    [InlineData("  AI.UNCLASSIFIED  ")]
    public void TryParse_AcceptsWellFormedIds(string value)
    {
        Assert.True(ReviewRuleId.TryParse(value, out var id));
        Assert.Equal(value.Trim(), id.Value);
        Assert.Equal(value.Trim(), id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SECURITY")]
    [InlineData("security.exposed_secret")]
    [InlineData("Security.Exposed")]
    [InlineData("SECURITY..SECRET")]
    [InlineData(".SECURITY.SECRET")]
    [InlineData("SECURITY.SECRET.")]
    [InlineData("SECURITY.1SECRET")]
    [InlineData("SECURITY._SECRET")]
    [InlineData("SECURITY.EXPOSED-SECRET")]
    [InlineData("SECURITY.EXPOSED SECRET")]
    [InlineData("A.B.C.D.E.F.G")]
    [InlineData("SÉCURITÉ.SECRET")]
    public void TryParse_RejectsMalformedIds(string? value) =>
        Assert.False(ReviewRuleId.TryParse(value, out _));

    [Fact]
    public void TryParse_RejectsOverlongIds()
    {
        var id = "A." + new string('B', ReviewRuleId.MaxLength);

        Assert.False(ReviewRuleId.TryParse(id, out _));
    }

    [Fact]
    public void Parse_WithMalformedId_ThrowsWithTheValue()
    {
        var ex = Assert.Throws<ArgumentException>(() => ReviewRuleId.Parse("not a rule"));

        Assert.Contains("not a rule", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ids_HaveValueEquality()
    {
        Assert.Equal(ReviewRuleId.Parse("SECURITY.SQL_INJECTION"), ReviewRuleId.Parse(" SECURITY.SQL_INJECTION "));
        Assert.NotEqual(ReviewRuleId.Parse("SECURITY.SQL_INJECTION"), ReviewRuleId.Parse("SECURITY.PATH_TRAVERSAL"));
        Assert.Single(new HashSet<ReviewRuleId> { ReviewRuleId.Parse("A.B"), ReviewRuleId.Parse("A.B") });
    }
}
