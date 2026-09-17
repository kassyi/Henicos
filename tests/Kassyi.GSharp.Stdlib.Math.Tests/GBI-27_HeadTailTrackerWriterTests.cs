using Kassyi.GSharp.Stdlib.Math.IO;

namespace Kassyi.GSharp.Stdlib.Math.Tests.IO;

public class GBI27_HeadTailTrackerWriterTests
{
    [SkippableTheory]
    [Trait("FeatureId", "GBI-27")]
    [Trait("Category", "HappyPath")]
    [InlineData("12345", 5, 5, "12345", "12345", "12345")]
    [InlineData("1234567890", 5, 5, "12345", "67890", "1234567890")]
    [InlineData("1234567890123", 5, 5, "12345", "90123", "12345...90123")]
    public void Test_GBI27_Write_String_TracksHeadAndTailCorrectly(string input, int headCap, int tailCap, string expectedHead, string expectedTail, string expectedPreview)
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
    [Trait("FeatureId", "GBI-27")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBI27_Write_ZeroCapacity_DoesNotThrowAndTracksLength()
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
    [Trait("FeatureId", "GBI-27")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBI27_Write_CharByChar_MatchesStringOutput()
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
}
