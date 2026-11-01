using Xunit;
using Kassyi.GSharp.Cryptography;

namespace Kassyi.GSharp.Tests.Shared.Cryptography;

[Category(TestCategory.Unit)]
public class Ed25519Tests
{
    [Fact]
    public void Test_DefaultDevKey_Matches_Rfc8032_TestVector()
    {
        // Assert the default dev key logic
        var publicKey = Ed25519.GetPublicKey(Ed25519.DefaultDevKeySeed);
        Assert.Equal(Ed25519.DefaultDevPublicKey.ToArray(), publicKey);
    }

    [Fact]
    public void Test_GetPublicKey_InvalidSeedLength_ThrowsArgumentException()
    {
        var invalidSeed = new byte[31];
        Assert.Throws<ArgumentException>(() => Ed25519.GetPublicKey(invalidSeed));

        var invalidSeedLong = new byte[33];
        Assert.Throws<ArgumentException>(() => Ed25519.GetPublicKey(invalidSeedLong));
    }

    [Fact]
    public void Test_Sign_InvalidSeedLength_ThrowsArgumentException()
    {
        var invalidSeed = new byte[31];
        Assert.Throws<ArgumentException>(() => Ed25519.Sign("Hello World"u8.ToArray(), invalidSeed));
    }

    [Fact]
    public void Test_Verify_InvalidLengths_ReturnsFalse()
    {
        var message = "Hello World"u8.ToArray();
        var validSeed = Ed25519.DefaultDevKeySeed;
        var validPubKey = Ed25519.GetPublicKey(validSeed);
        var validSignature = Ed25519.Sign(message, validSeed);

        // Invalid signature length
        Assert.False(Ed25519.Verify(message, new byte[63], validPubKey));
        Assert.False(Ed25519.Verify(message, new byte[65], validPubKey));

        // Invalid public key length
        Assert.False(Ed25519.Verify(message, validSignature, new byte[31]));
        Assert.False(Ed25519.Verify(message, validSignature, new byte[33]));
    }

    [Fact]
    public void Test_Verify_InvalidPublicKeyPoint_ReturnsFalse()
    {
        var message = "Hello World"u8.ToArray();
        var validSeed = Ed25519.DefaultDevKeySeed;
        var validSignature = Ed25519.Sign(message, validSeed);

        // A public key consisting of 32 bytes of 0xFF will likely fail TryDecodePoint (y >= s_p or similar)
        var invalidPubKey = new byte[32];
        Array.Fill(invalidPubKey, (byte)0xFF);

        Assert.False(Ed25519.Verify(message, validSignature, invalidPubKey));
    }

    [Fact]
    public void Test_Verify_InvalidSignatureS_ReturnsFalse()
    {
        var message = "Hello World"u8.ToArray();
        var validSeed = Ed25519.DefaultDevKeySeed;
        var validPubKey = Ed25519.GetPublicKey(validSeed);
        var validSignature = Ed25519.Sign(message, validSeed);

        // Modify the S part of the signature to be >= s_l
        // s_l is 2^252 + 27742317777372353535851937790883648493, which is 32 bytes
        // Filling the last 32 bytes with 0xFF will definitely make it larger than s_l
        var invalidSignature = validSignature.ToArray();
        for (int i = 32; i < 64; i++)
        {
            invalidSignature[i] = 0xFF;
        }

        Assert.False(Ed25519.Verify(message, invalidSignature, validPubKey));
    }

    [Fact]
    public void Test_Verify_InvalidSignatureRPoint_ReturnsFalse()
    {
        var message = "Hello World"u8.ToArray();
        var validSeed = Ed25519.DefaultDevKeySeed;
        var validPubKey = Ed25519.GetPublicKey(validSeed);
        var validSignature = Ed25519.Sign(message, validSeed);

        // Modify the R part of the signature to fail TryDecodePoint
        var invalidSignature = validSignature.ToArray();
        for (int i = 0; i < 32; i++)
        {
            invalidSignature[i] = 0xFF; // y >= s_p
        }

        Assert.False(Ed25519.Verify(message, invalidSignature, validPubKey));
    }

    [Fact]
    public void Test_Verify_WrongMessageOrKey_ReturnsFalse()
    {
        var message = "Hello World"u8.ToArray();
        var wrongMessage = "Hello World2"u8.ToArray();
        var validSeed = Ed25519.DefaultDevKeySeed;
        var validPubKey = Ed25519.GetPublicKey(validSeed);
        var validSignature = Ed25519.Sign(message, validSeed);

        // Wrong message
        Assert.False(Ed25519.Verify(wrongMessage, validSignature, validPubKey));

        // Wrong key
        var wrongSeed = new byte[32];
        new Random(42).NextBytes(wrongSeed);
        var wrongPubKey = Ed25519.GetPublicKey(wrongSeed);
        Assert.False(Ed25519.Verify(message, validSignature, wrongPubKey));

        // Wrong signature
        var wrongSignature = Ed25519.Sign(message, wrongSeed);
        Assert.False(Ed25519.Verify(message, wrongSignature, validPubKey));
    }

    [Fact]
    public void Test_Verify_ValidSignature_ReturnsTrue()
    {
        var message = "A normal test message"u8.ToArray();
        var validSeed = new byte[32];
        new Random(43).NextBytes(validSeed);
        var validPubKey = Ed25519.GetPublicKey(validSeed);
        var validSignature = Ed25519.Sign(message, validSeed);

        Assert.True(Ed25519.Verify(message, validSignature, validPubKey));
    }
}
