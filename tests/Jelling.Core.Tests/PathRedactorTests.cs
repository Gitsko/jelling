namespace Jelling.Core.Tests;

public class PathRedactorTests
{
    private static readonly PathRedactor Redactor = new([@"C:\Users\bob", @"C:\Users\BOB~1"], "%USERPROFILE%");

    [Fact]
    public void ReplacesTheFolderInAPath()
    {
        Assert.Equal(@"Could not find '%USERPROFILE%\AppData\x.json'", Redactor.Redact(@"Could not find 'C:\Users\bob\AppData\x.json'"));
    }

    [Fact]
    public void IgnoresCase()
    {
        Assert.Equal(@"%USERPROFILE%\x", Redactor.Redact(@"c:\users\Bob\x"));
    }

    [Theory]
    [InlineData(@"C:\Users\bob.old\x")]
    [InlineData(@"C:\Users\bob-work\x")]
    public void LeavesAFolderNameContinuedByADotOrHyphen(string text)
    {
        Assert.Equal(text, Redactor.Redact(text));
    }

    [Fact]
    public void ReplacesTheShortForm()
    {
        Assert.Equal(@"%USERPROFILE%\AppData\Local\Temp", Redactor.Redact(@"C:\Users\BOB~1\AppData\Local\Temp"));
    }

    [Fact]
    public void ReplacesForwardSlashes()
    {
        Assert.Equal("file:///%USERPROFILE%/x.txt", Redactor.Redact("file:///C:/Users/bob/x.txt"));
    }

    [Fact]
    public void LeavesALongerFolderName()
    {
        Assert.Equal(@"C:\Users\bobby\x", Redactor.Redact(@"C:\Users\bobby\x"));
    }

    [Fact]
    public void ReplacesTheFolderAtTheEnd()
    {
        Assert.Equal("in %USERPROFILE%", Redactor.Redact(@"in C:\Users\bob"));
    }

    [Fact]
    public void WithoutFoldersLeavesTheText()
    {
        Assert.Equal(@"C:\Users\bob", new PathRedactor(["", ""], "x").Redact(@"C:\Users\bob"));
    }
}
