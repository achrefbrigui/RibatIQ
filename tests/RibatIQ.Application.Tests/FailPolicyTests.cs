using RibatIQ.Application.Configuration;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Tests;

public sealed class FailPolicyTests
{
    [Theory]
    [InlineData("info", FindingSeverity.Info)]
    [InlineData("low", FindingSeverity.Low)]
    [InlineData("Medium", FindingSeverity.Medium)]
    [InlineData(" HIGH ", FindingSeverity.High)]
    [InlineData("critical", FindingSeverity.Critical)]
    public void TryParse_WithSeverity_ReturnsThreshold(string value, FindingSeverity expected)
    {
        Assert.True(FailPolicy.TryParse(value, out var policy));
        Assert.Equal(expected, policy.Threshold);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("NONE")]
    public void TryParse_WithNone_ReturnsNever(string value)
    {
        Assert.True(FailPolicy.TryParse(value, out var policy));
        Assert.Same(FailPolicy.Never, policy);
        Assert.Null(policy.Threshold);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("severe")]
    [InlineData("3")]
    public void TryParse_WithUnknownValue_Fails(string? value) =>
        Assert.False(FailPolicy.TryParse(value, out _));

    [Fact]
    public void AcceptedValues_RoundTripThroughToString()
    {
        Assert.Equal(["none", "info", "low", "medium", "high", "critical"], FailPolicy.AcceptedValues);

        foreach (var value in FailPolicy.AcceptedValues)
        {
            Assert.True(FailPolicy.TryParse(value, out var policy));
            Assert.Equal(value, policy.ToString());
        }
    }

    [Fact]
    public void AtOrAbove_WithUndefinedSeverity_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FailPolicy.AtOrAbove((FindingSeverity)99));
}
