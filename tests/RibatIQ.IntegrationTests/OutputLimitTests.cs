using RibatIQ.Application;
using RibatIQ.Infrastructure.Git;
using RibatIQ.Infrastructure.Processes;

namespace RibatIQ.IntegrationTests;

/// <summary>External tool output is bounded, so a runaway tool or an enormous change cannot exhaust memory.</summary>
public sealed class OutputLimitTests : IDisposable
{
    private readonly TempGitRepository _repo = new();

    public void Dispose() => _repo.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BoundedOutput_UnderTheLimit_ReturnsEverything()
    {
        var text = new string('a', 40_000);

        var result = await BoundedOutput.ReadAsync(new StringReader(text), 40_000, Ct);

        Assert.False(result.Exceeded);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public async Task BoundedOutput_OverTheLimit_KeepsNothing_AndDrainsTheStream()
    {
        var reader = new StringReader(new string('a', 40_001));

        var result = await BoundedOutput.ReadAsync(reader, 40_000, Ct);

        Assert.True(result.Exceeded);
        Assert.Empty(result.Text);
        Assert.Equal(-1, reader.Peek());
    }

    [Fact]
    public async Task Git_OutputOverTheLimit_FailsTheOperation()
    {
        _repo.WriteFile("a.cs", "class A {}\n");
        _repo.Commit("base");
        _repo.Git("switch", "-q", "-c", "feature");
        _repo.WriteFile("b.cs", string.Concat(Enumerable.Repeat("class B {}\n", 200)));
        _repo.Commit("big change");

        var git = new GitCli(maxOutputChars: 1_000);

        var ex = await Assert.ThrowsAsync<OperationFailedException>(() => git.GetDiffAsync(_repo.Path, "main", "feature", Ct));
        Assert.Contains("too large to review", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Git_OutputUnderTheLimit_IsReturned()
    {
        _repo.WriteFile("a.cs", "class A {}\n");
        _repo.Commit("base");

        var text = await new GitCli(maxOutputChars: 1_000).ReadFileAsync(_repo.Path, "HEAD", "a.cs", Ct);

        Assert.Equal("class A {}\n", text);
    }
}
