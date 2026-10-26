using System;
using Kassyi.GSharp.Runtime.Fatbin;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime.Fatbin;

[Category(TestCategory.Runtime)]
public class FatbinImageTests
{
    [Fact]
    public void IsCompatibleWith_InvalidDevice_ReturnsFalse()
    {
        var image = new FatbinImage(
            magic: 0xBA55ED50,
            version: 1,
            payloadSize: 100,
            hasGSharpSignature: false,
            payload: ReadOnlyMemory<byte>.Empty,
            signature: ReadOnlyMemory<byte>.Empty,
            items: Array.Empty<FatbinItemInfo>());

        Assert.False(image.IsCompatibleWith(-1, 0));
        Assert.False(image.IsCompatibleWith(8, -1));
    }

    [Fact]
    public void VerifySignature_InvalidLengthOrNoSignature_ReturnsFalse()
    {
        var image = new FatbinImage(
            magic: 0xBA55ED50,
            version: 1,
            payloadSize: 100,
            hasGSharpSignature: false,
            payload: ReadOnlyMemory<byte>.Empty,
            signature: ReadOnlyMemory<byte>.Empty,
            items: Array.Empty<FatbinItemInfo>());

        var key = new byte[32].AsSpan();
        Assert.False(image.VerifySignature(key)); // No signature

        var signedImage = new FatbinImage(
            magic: 0xBA55ED50,
            version: 1,
            payloadSize: 100,
            hasGSharpSignature: true,
            payload: ReadOnlyMemory<byte>.Empty,
            signature: new byte[10], // Bad length
            items: Array.Empty<FatbinItemInfo>());

        Assert.False(signedImage.VerifySignature(key));
    }

    [Fact]
    public void VerifySignature_InvalidKeySpan_ReturnsFalse()
    {
        var signedImage = new FatbinImage(
            magic: 0xBA55ED50,
            version: 1,
            payloadSize: 100,
            hasGSharpSignature: true,
            payload: ReadOnlyMemory<byte>.Empty,
            signature: new byte[64],
            items: Array.Empty<FatbinItemInfo>());

        // Key span length is not a multiple of 32
        var badKeySpan = new byte[33].AsSpan();
        Assert.False(signedImage.VerifySignature(badKeySpan));
    }
}
