using System;
using Kassyi.GSharp.Runtime.Fatbin;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime.Fatbin;

public class GpuSecurityPolicyTests
{
    [Fact]
    public void RegisterTrustedKey_InvalidLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => GpuSecurityPolicy.RegisterTrustedKey(new byte[10].AsSpan()));
    }

    [Fact]
    public void VerifyImage_NullImage_ThrowsArgumentNullException()
    {
        // [WHY] Intentionally pass null to verify ArgumentNullException guard in VerifyImage.
        Assert.Throws<ArgumentNullException>(() => GpuSecurityPolicy.VerifyImage(null!));
    }

    [Fact]
    public void RegisterTrustedKey_ValidKey_DoesNotThrow()
    {
        // RFC 8032 Section 7.1 Test Vector 2 public key
        var dummyKey = Convert.FromHexString("3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c");

        Assert.False(GpuSecurityPolicy.EmbeddedKeys.IsEmpty);

        // This validates the array expansion in the locking block
        var ex = Record.Exception(() => GpuSecurityPolicy.RegisterTrustedKey(dummyKey.AsSpan()));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("0100000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000080")]
    [InlineData("26e8958fc2b227b045c3f489f2ef98f0d5dfac05d3c63339b13802886d53fc05")]
    [InlineData("26e8958fc2b227b045c3f489f2ef98f0d5dfac05d3c63339b13802886d53fc85")]
    [InlineData("c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac037a")]
    [InlineData("c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac03fa")]
    public void RegisterTrustedKey_SmallOrderKey_ThrowsArgumentException(string hex)
    {
        var ex = Assert.Throws<ArgumentException>(() => GpuSecurityPolicy.RegisterTrustedKey(Convert.FromHexString(hex)));

        Assert.Contains("small-order", ex.Message);
    }

    [Theory]
    [InlineData("edffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("0200000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("abababababababababababababababababababababababababababababababab")]
    public void RegisterTrustedKey_NotDecompressibleKey_ThrowsArgumentException(string hex)
    {
        var ex = Assert.Throws<ArgumentException>(() => GpuSecurityPolicy.RegisterTrustedKey(Convert.FromHexString(hex)));

        Assert.Contains("not a valid Ed25519 public key", ex.Message);
    }
}

