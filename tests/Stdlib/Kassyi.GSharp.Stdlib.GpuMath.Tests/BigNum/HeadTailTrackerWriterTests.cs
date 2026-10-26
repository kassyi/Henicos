namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class HeadTailTrackerWriterTests
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("12345", 5, 5, "12345", "12345", "12345")]
    [InlineData("1234567890", 5, 5, "12345", "67890", "1234567890")]
    [InlineData("1234567890123", 5, 5, "12345", "90123", "12345...90123")]
    public void Test_Write_String_TracksHeadAndTailCorrectly(string input, int headCap, int tailCap, string expectedHead, string expectedTail, string expectedPreview)
    {
        CudaCapability.EnsureGpuAvailable();
        using var writer = new HeadTailTrackerWriter(headCap, tailCap);
        writer.Write(input);

        Assert.Equal(input.Length, writer.TotalCharactersWritten);
        Assert.Equal(expectedHead, writer.GetHead());
        Assert.Equal(expectedTail, writer.GetTail());
        Assert.Equal(expectedPreview, writer.GetPreview());
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Write_ZeroCapacity_DoesNotThrowAndTracksLength()
    {
        CudaCapability.EnsureGpuAvailable();
        using var writer = new HeadTailTrackerWriter(0, 0);
        writer.Write("1234567890");

        Assert.Equal(10, writer.TotalCharactersWritten);
        Assert.Equal(string.Empty, writer.GetHead());
        Assert.Equal(string.Empty, writer.GetTail());
        Assert.Equal("...", writer.GetPreview());
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Write_CharByChar_MatchesStringOutput()
    {
        CudaCapability.EnsureGpuAvailable();
        using var writer = new HeadTailTrackerWriter(3, 3);
        var input = "abcdefghijklmnopqrstuvwxyz";
        foreach (var c in input)
        {
            writer.Write(c);
        }

        Assert.Equal(26, writer.TotalCharactersWritten);
        Assert.Equal("abc", writer.GetHead());
        Assert.Equal("xyz", writer.GetTail());
        Assert.Equal("abc...xyz", writer.GetPreview());
    }

    // The writer is host-only; the tests below need no GPU.

    [Theory]
    [Category(TestCategory.Stdlib)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("abc", 5, 5, "abc", "abc", "abc")]
    [InlineData("abcde", 3, 3, "abc", "cde", "abcde")]
    [InlineData("abcdef", 3, 3, "abc", "def", "abcdef")]
    public void Write_UpToHeadPlusTail_PreviewIsWholeInput(string input, int headCap, int tailCap, string expectedHead, string expectedTail, string expectedPreview)
    {
        // While the input fits in head + tail, the preview is the whole input with no separator.
        // mutant: HeadTailTrackerWriter.cs:124 `return _head.ToString()` removed (the tail slice of -2 throws)
        // mutant: HeadTailTrackerWriter.cs:99 `return new string(_tailBuffer, 0, _tailIndex)` removed (the unfilled tail is rotated)
        using var writer = new HeadTailTrackerWriter(headCap, tailCap);
        writer.Write(input);

        Assert.Equal(
            (input.Length, expectedHead, expectedTail, expectedPreview),
            (writer.TotalCharactersWritten, writer.GetHead(), writer.GetTail(), writer.GetPreview())
        );
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Write_NullOrEmptyString_IsNoOp()
    {
        // A null or empty string writes nothing.
        // mutant: HeadTailTrackerWriter.cs:62 `return` removed (foreach over null throws)
        using var writer = new HeadTailTrackerWriter(5, 5);
        writer.Write((string)null);
        writer.Write(string.Empty);

        Assert.Equal((0L, string.Empty, string.Empty), (writer.TotalCharactersWritten, writer.GetHead(), writer.GetTail()));
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Write_CharArraySegment_WritesOnlySegment()
    {
        // Write(buffer, index, count) writes buffer[index..index+count) and nothing else.
        // mutant: HeadTailTrackerWriter.cs:74 `i < count` -> `i <= count` / `i < count` -> `i > count`
        // mutant: HeadTailTrackerWriter.cs:76 `index + i` -> `index - i`
        using var writer = new HeadTailTrackerWriter(5, 5);
        writer.Write("wxyz".ToCharArray(), 1, 2);

        Assert.Equal((2L, "xy", "xy"), (writer.TotalCharactersWritten, writer.GetHead(), writer.GetTail()));
    }
}
