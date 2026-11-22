using RibatIQ.Application.Configuration;
using RibatIQ.Application.Reviewing;
using RibatIQ.Application.Scanning;

namespace RibatIQ.Application.Tests;

public sealed class RequestTests
{
    [Theory]
    [InlineData("origin/main", "HEAD")]
    [InlineData("HEAD~3", "feature/my-branch")]
    [InlineData("a1b2c3d", "v1.2.0")]
    public void ReviewRequest_WithRevisions_KeepsThem(string baseRef, string headRef)
    {
        var request = new ReviewRequest(baseRef, headRef);

        Assert.Equal(baseRef, request.BaseRef);
        Assert.Equal(headRef, request.HeadRef);
        Assert.Equal($"{baseRef}...{headRef}", request.Range);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("-n")]
    [InlineData("--output=/tmp/x")]
    [InlineData("main branch")]
    [InlineData("main\nHEAD")]
    public void ReviewRequest_WithUnsafeRevision_Throws(string value)
    {
        Assert.NotNull(ReviewRequest.GetRefError(value));
        Assert.Throws<ArgumentException>(() => new ReviewRequest(value, "HEAD"));
        Assert.Throws<ArgumentException>(() => new ReviewRequest("main", value));
    }

    [Fact]
    public void ReviewRequest_WithOverlongRevision_Throws() =>
        Assert.Throws<ArgumentException>(() => new ReviewRequest(new string('a', ReviewRequest.MaxRefLength + 1), "HEAD"));

    [Fact]
    public void ReviewRequest_TrimsRevisions() =>
        Assert.Equal("main", new ReviewRequest(" main ", "HEAD").BaseRef);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ScanRequest_WithBlankPath_Throws(string path) =>
        Assert.Throws<ArgumentException>(() => new ScanRequest(path));

    [Theory]
    [InlineData("openai", "openai")]
    [InlineData("Anthropic", "anthropic")]
    [InlineData(" OPENAI ", "openai")]
    [InlineData("gemini", null)]
    [InlineData(null, null)]
    public void LlmProviders_Normalize(string? value, string? expected) =>
        Assert.Equal(expected, LlmProviders.Normalize(value));
}
