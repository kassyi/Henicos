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

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("", 0xcbf29ce484222325UL)]
    [InlineData("a", 0xaf63dc4c8601ec8cUL)]
    [InlineData("foobar", 0x85944171f73967e8UL)]
    [InlineData("MyTestKernel_123", 0xc3b5807dff6beaddUL)]
    // Inputs of real snapshot names: Snap.AddKernel_7c2f18f1da_*, Snap.NestedScopeAndIndentation_87beb54344_*
    [InlineData("AddKernel", 0x7c2f18f1daf67d19UL)]
    [InlineData("Test_SourceWriter_NestedScopeAndIndentation_Snapshots", 0x87beb543447b8cecUL)]
    // Hashed per UTF-16 code unit, not per UTF-8 byte
    [InlineData("héllo", 0x0b25d480829d74bfUL)]
    [InlineData("日本語", 0xc964a9c78e5bd62eUL)]
    [InlineData("😀", 0xe5e45a0a241b88d8UL)]
    public void ComputeFnv1A64_GivenInput_ReturnsPinnedFullHash(string text, ulong expected)
    {
        Assert.Equal(expected, HashHelper.ComputeFnv1A64(text));
        Assert.Equal(expected.ToString("x16")[..10], HashHelper.ComputeFnv1A64ShortHash(text));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ComputeFnv1A64_GivenLongString_ReturnsPinnedFullHash()
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < 1000; i++)
        {
            builder.Append("abc");
        }

        Assert.Equal(0xbcb5b1cc8e5e9165UL, HashHelper.ComputeFnv1A64(builder.ToString()));
    }
}
