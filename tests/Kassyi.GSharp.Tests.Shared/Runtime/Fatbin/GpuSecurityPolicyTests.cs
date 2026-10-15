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
        var dummyKey = new byte[32];
        Array.Fill(dummyKey, (byte)0xAB);

        Assert.False(GpuSecurityPolicy.EmbeddedKeys.IsEmpty);

        // This validates the array expansion in the locking block
        var ex = Record.Exception(() => GpuSecurityPolicy.RegisterTrustedKey(dummyKey.AsSpan()));
        Assert.Null(ex);
    }
}

