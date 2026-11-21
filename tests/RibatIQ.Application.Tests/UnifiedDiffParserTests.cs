using RibatIQ.Application.Reviewing;

namespace RibatIQ.Application.Tests;

public sealed class UnifiedDiffParserTests
{
    [Fact]
    public void Parse_EmptyDiff_ReturnsNoChanges() =>
        Assert.Empty(UnifiedDiffParser.Parse(string.Empty));

    [Fact]
    public void Parse_ModifiedFile_WithZeroContext_ReturnsAddedRanges()
    {
        var changes = UnifiedDiffParser.Parse("""
            diff --git a/src/App/Service.cs b/src/App/Service.cs
            index 1111111..2222222 100644
            --- a/src/App/Service.cs
            +++ b/src/App/Service.cs
            @@ -3 +3 @@ public class Service
            -    old
            +    new
            @@ -10,0 +11,2 @@ public class Service
            +    added one
            +    added two
            @@ -20,3 +22,0 @@ public class Service
            -    removed
            -    removed
            -    removed
            """);

        var change = Assert.Single(changes);
        Assert.Equal("src/App/Service.cs", change.Path);
        Assert.Equal(ChangeKind.Modified, change.Kind);
        Assert.Null(change.OldPath);
        Assert.Equal([new LineRange(3, 3), new LineRange(11, 12)], change.AddedLines);
    }

    [Fact]
    public void Parse_WithContextLines_TracksLineNumbersThroughTheHunk()
    {
        var changes = UnifiedDiffParser.Parse("""
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            @@ -1,5 +1,6 @@
             line 1
            -line 2
            +line 2 changed
            +line 2b
             line 3
             line 4
            """);

        Assert.Equal([new LineRange(2, 3)], Assert.Single(changes).AddedLines);
    }

    [Fact]
    public void Parse_AddedLinesThatLookLikeHeaders_AreTreatedAsContent()
    {
        var changes = UnifiedDiffParser.Parse("""
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            @@ -0,0 +1,3 @@
            +++ b/not-a-header.cs
            +diff --git a/x b/x
            +--- comment
            """);

        var change = Assert.Single(changes);
        Assert.Equal("a.cs", change.Path);
        Assert.Equal([new LineRange(1, 3)], change.AddedLines);
    }

    [Fact]
    public void Parse_NewFile_IsAdded()
    {
        var change = Assert.Single(UnifiedDiffParser.Parse("""
            diff --git a/src/New.cs b/src/New.cs
            new file mode 100644
            index 0000000..3333333
            --- /dev/null
            +++ b/src/New.cs
            @@ -0,0 +1,2 @@
            +class New
            +{}
            """));

        Assert.Equal(ChangeKind.Added, change.Kind);
        Assert.Equal("src/New.cs", change.Path);
        Assert.Equal([new LineRange(1, 2)], change.AddedLines);
    }

    [Fact]
    public void Parse_DeletedFile_IsDeleted_WithNoAddedLines()
    {
        var change = Assert.Single(UnifiedDiffParser.Parse("""
            diff --git a/src/Old.cs b/src/Old.cs
            deleted file mode 100644
            index 3333333..0000000
            --- a/src/Old.cs
            +++ /dev/null
            @@ -1,2 +0,0 @@
            -class Old
            -{}
            """));

        Assert.Equal(ChangeKind.Deleted, change.Kind);
        Assert.Equal("src/Old.cs", change.Path);
        Assert.Empty(change.AddedLines);
    }

    [Fact]
    public void Parse_PureRename_HasNewPathAndOldPath()
    {
        var change = Assert.Single(UnifiedDiffParser.Parse("""
            diff --git a/src/Old Name.cs b/src/New Name.cs
            similarity index 100%
            rename from src/Old Name.cs
            rename to src/New Name.cs
            """));

        Assert.Equal(ChangeKind.Renamed, change.Kind);
        Assert.Equal("src/New Name.cs", change.Path);
        Assert.Equal("src/Old Name.cs", change.OldPath);
        Assert.Empty(change.AddedLines);
    }

    [Fact]
    public void Parse_RenameWithEdits_HasAddedLines()
    {
        var change = Assert.Single(UnifiedDiffParser.Parse("""
            diff --git a/a.cs b/b.cs
            similarity index 90%
            rename from a.cs
            rename to b.cs
            index 1111111..2222222 100644
            --- a/a.cs
            +++ b/b.cs
            @@ -2 +2 @@
            -x
            +y
            """));

        Assert.Equal(ChangeKind.Renamed, change.Kind);
        Assert.Equal("b.cs", change.Path);
        Assert.Equal("a.cs", change.OldPath);
        Assert.Equal([new LineRange(2, 2)], change.AddedLines);
    }

    [Fact]
    public void Parse_QuotedPaths_AreUnescaped()
    {
        var change = Assert.Single(UnifiedDiffParser.Parse("""
            diff --git "a/src/tab\there.cs" "b/src/tab\there.cs"
            --- "a/src/tab\there.cs"
            +++ "b/src/caf\303\251 \"q\".cs"
            @@ -1 +1 @@
            -a
            +b
            """));

        Assert.Equal("src/café \"q\".cs", change.Path);
    }

    [Fact]
    public void Parse_BinaryFile_WithoutPatch_IsSkipped()
    {
        var changes = UnifiedDiffParser.Parse("""
            diff --git a/logo.png b/logo.png
            index 1111111..2222222 100644
            Binary files a/logo.png and b/logo.png differ
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            @@ -1 +1 @@
            -a
            +b
            """);

        Assert.Equal(["a.cs"], changes.Select(c => c.Path));
    }

    [Fact]
    public void Parse_NoNewlineMarker_IsIgnored()
    {
        var change = Assert.Single(UnifiedDiffParser.Parse("""
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            @@ -1 +1,2 @@
            -end
            \ No newline at end of file
            +end
            +more
            \ No newline at end of file
            """));

        Assert.Equal([new LineRange(1, 2)], change.AddedLines);
    }

    [Fact]
    public void Parse_CrLfLineEndings_AreSupported()
    {
        var diff = "diff --git a/a.cs b/a.cs\r\n--- a/a.cs\r\n+++ b/a.cs\r\n@@ -1 +1 @@\r\n-a\r\n+b\r\n";

        Assert.Equal([new LineRange(1, 1)], Assert.Single(UnifiedDiffParser.Parse(diff)).AddedLines);
    }

    [Fact]
    public void Parse_MultipleFiles_KeepsThemApart()
    {
        var changes = UnifiedDiffParser.Parse("""
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            @@ -1 +1 @@
            -a
            +b
            diff --git a/b.cs b/b.cs
            --- a/b.cs
            +++ b/b.cs
            @@ -5,0 +6 @@
            +c
            """);

        Assert.Equal(["a.cs", "b.cs"], changes.Select(c => c.Path));
        Assert.Equal([new LineRange(6, 6)], changes[1].AddedLines);
    }

    [Theory]
    [InlineData("plain/path.cs", "plain/path.cs")]
    [InlineData("\"a\\\\b.cs\"", "a\\b.cs")]
    [InlineData("\"new\\nline.cs\"", "new\nline.cs")]
    [InlineData("\"tab.cs\"\t", "tab.cs")]
    public void Unquote_DecodesGitQuoting(string quoted, string expected) =>
        Assert.Equal(expected, UnifiedDiffParser.Unquote(quoted));
}
