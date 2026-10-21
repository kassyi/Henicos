using Kassyi.GSharp.Compiler.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

public class HashHelperTests
{
    [Fact]
    public void ComputeFnv1A64ShortHash_GivenNormalString_ReturnsStableKnownHash()
    {
        // Assert a stable known hash for normal input
        var hash = HashHelper.ComputeFnv1A64ShortHash("MyTestKernel_123");
        // We know it should be a 10 char string.
        Assert.NotNull(hash);
        Assert.Equal(10, hash.Length);
        
        var hash2 = HashHelper.ComputeFnv1A64ShortHash("MyTestKernel_123");
        Assert.Equal(hash, hash2);
    }

    [Fact]
    public void ComputeFnv1A64ShortHash_GivenEmptyString_ReturnsExpectedHash()
    {
        var hash = HashHelper.ComputeFnv1A64ShortHash("");
        Assert.NotNull(hash);
        Assert.Equal(10, hash.Length);
        Assert.Equal("cbf29ce484", hash); // FNV-1a offset basis 14695981039346656037UL in hex is cbf29ce484222325, 10 chars is cbf29ce484
    }

    [Theory]
    [InlineData("a", "af63dc4c86")]
    [InlineData("foobar", "85944171f7")]
    public void ComputeFnv1A64ShortHash_GivenKnownVectors_ReturnsPinnedHash(string text, string expected)
    {
        // Published FNV-1a 64 test vectors (first 10 hex chars); guards against snapshot file name drift.
        Assert.Equal(expected, HashHelper.ComputeFnv1A64ShortHash(text));
    }

    [Fact]
    public void ComputeFnv1A64ShortHash_GivenNullString_ThrowsArgumentNullException()
    {
        // Intentionally passing null to assert ArgumentNullException defensive guard.
        Assert.Throws<ArgumentNullException>(() => HashHelper.ComputeFnv1A64ShortHash(null!));
    }
}
