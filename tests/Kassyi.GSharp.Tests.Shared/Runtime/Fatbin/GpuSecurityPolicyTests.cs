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
        Assert.Throws<ArgumentNullException>(() => GpuSecurityPolicy.VerifyImage(null!));
    }

    [Fact]
    public void RegisterTrustedKey_ValidKey_DoesNotThrow()
    {
        var dummyKey = new byte[32];
        Array.Fill(dummyKey, (byte)0xAB);
        
        // This validates the array expansion in the locking block
        GpuSecurityPolicy.RegisterTrustedKey(dummyKey.AsSpan());
    }
}
