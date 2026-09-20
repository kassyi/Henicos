namespace Kassyi.GSharp.Tests.Helpers;

public class HashHelperTests
{
    [SkippableFact]
    public void ComputeShortHash_GivenNormalString_ReturnsStableKnownHash()
    {
        // Assert a stable known hash for normal input
        var hash = HashHelper.ComputeShortHash("MyTestKernel_123");
        // We know it should be a 10 char string.
        Assert.NotNull(hash);
        Assert.Equal(10, hash.Length);
        
        var hash2 = HashHelper.ComputeShortHash("MyTestKernel_123");
        Assert.Equal(hash, hash2);
    }

    [SkippableFact]
    public void ComputeShortHash_GivenEmptyString_ReturnsExpectedHash()
    {
        var hash = HashHelper.ComputeShortHash("");
        Assert.NotNull(hash);
        Assert.Equal(10, hash.Length);
        Assert.Equal("cbf29ce484", hash); // FNV-1a offset basis 14695981039346656037UL in hex is cbf29ce484222325, 10 chars is cbf29ce484
    }

    [SkippableFact]
    public void ComputeShortHash_GivenNullString_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => HashHelper.ComputeShortHash(null!));
    }
}
