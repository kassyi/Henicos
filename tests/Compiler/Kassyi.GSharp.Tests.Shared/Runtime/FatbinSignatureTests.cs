#nullable enable

using System;
using System.Linq;
using Kassyi.GSharp.Runtime.Fatbin;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Runtime.FatbinParserTests;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class FatbinSignatureTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void VerifySignature_Rfc8032TestVector_MatchesSpecification()
    {
        // RFC 8032 Section 7.1 Test Vector 1 (Empty message)
        byte[] secretKeySeed = Convert.FromHexString(
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] expectedPublicKey = Convert.FromHexString(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        byte[] expectedSignature = Convert.FromHexString(
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e06522490155"
                + "5fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");

        byte[] actualPublicKey = Kassyi.GSharp.Cryptography.Ed25519.GetPublicKey(secretKeySeed);
        Assert.Equal(expectedPublicKey, actualPublicKey);

        byte[] actualSignature = Kassyi.GSharp.Cryptography.Ed25519.Sign(ReadOnlySpan<byte>.Empty, secretKeySeed);
        Assert.Equal(expectedSignature, actualSignature);

        bool isValid = Kassyi.GSharp.Cryptography.Ed25519.Verify(
            ReadOnlySpan<byte>.Empty,
            actualSignature,
            actualPublicKey
        );
        Assert.True(isValid);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryParse_SignedFatbinWithEd25519Signature_VerifiesSuccessfully()
    {
        // Generate a 32-byte secret key seed and corresponding Ed25519 signature
        byte[] secretKeySeed = new byte[32];
        Array.Fill(secretKeySeed, (byte)0x42);
        byte[] publicKey = Kassyi.GSharp.Cryptography.Ed25519.GetPublicKey(secretKeySeed);

        byte[] signature = Kassyi.GSharp.Cryptography.Ed25519.Sign(RealNvcc_DummyFatbin, secretKeySeed);
        byte[] signedBinary = RealNvcc_DummyFatbin.Concat(signature).ToArray();

        bool success = FatbinParser.TryParse(signedBinary, out FatbinImage? image);

        Assert.True(success);
        Assert.NotNull(image);
        Assert.True(image.HasGSharpSignature);
        Assert.Equal(64, image.Signature.Length);
        Assert.True(signature.AsSpan().SequenceEqual(image.Signature.Span));
        Assert.Equal(RealNvcc_DummyFatbin.Length, image.Payload.Length);
        Assert.True(RealNvcc_DummyFatbin.AsSpan().SequenceEqual(image.Payload.Span));

        // Verify signature against public key
        Assert.True(image.VerifySignature(publicKey));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifySignature_CorruptedPayload_ReturnsFalse()
    {
        byte[] secretKeySeed = new byte[32];
        Array.Fill(secretKeySeed, (byte)0x42);
        byte[] publicKey = Kassyi.GSharp.Cryptography.Ed25519.GetPublicKey(secretKeySeed);

        byte[] signature = Kassyi.GSharp.Cryptography.Ed25519.Sign(RealNvcc_DummyFatbin, secretKeySeed);
        byte[] signedBinary = RealNvcc_DummyFatbin.Concat(signature).ToArray();

        bool success = FatbinParser.TryParse(signedBinary, out FatbinImage? image);
        Assert.True(success);
        Assert.NotNull(image);

        // Tamper 1 byte in the payload data (offset 150, beyond headers) and construct corrupted fatbin image
        byte[] corruptedPayload = (byte[])RealNvcc_DummyFatbin.Clone();
        corruptedPayload[150] ^= 0xFF;

        byte[] corruptedBinary = corruptedPayload.Concat(signature).ToArray();
        bool parseCorrupted = FatbinParser.TryParse(corruptedBinary, out FatbinImage? corruptedImage);
        Assert.True(parseCorrupted);
        Assert.NotNull(corruptedImage);

        // Verification must strictly fail
        Assert.False(corruptedImage.VerifySignature(publicKey));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifySignature_WrongPublicKey_ReturnsFalse()
    {
        byte[] secretKeySeed = new byte[32];
        Array.Fill(secretKeySeed, (byte)0x42);
        byte[] wrongSeed = new byte[32];
        Array.Fill(wrongSeed, (byte)0x99);

        byte[] wrongPublicKey = Kassyi.GSharp.Cryptography.Ed25519.GetPublicKey(wrongSeed);

        byte[] signature = Kassyi.GSharp.Cryptography.Ed25519.Sign(RealNvcc_DummyFatbin, secretKeySeed);
        byte[] signedBinary = RealNvcc_DummyFatbin.Concat(signature).ToArray();

        bool success = FatbinParser.TryParse(signedBinary, out FatbinImage? image);
        Assert.True(success);
        Assert.NotNull(image);

        Assert.False(image.VerifySignature(wrongPublicKey));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void VerifySignature_AuthorizedKeysWhitelist_ReturnsTrue()
    {
        byte[] seed1 = new byte[32];
        Array.Fill(seed1, (byte)0x11);
        byte[] seed2 = new byte[32];
        Array.Fill(seed2, (byte)0x22);
        byte[] seed3 = new byte[32];
        Array.Fill(seed3, (byte)0x33);

        byte[] pubKey1 = Kassyi.GSharp.Cryptography.Ed25519.GetPublicKey(seed1);
        byte[] pubKey2 = Kassyi.GSharp.Cryptography.Ed25519.GetPublicKey(seed2);
        byte[] pubKey3 = Kassyi.GSharp.Cryptography.Ed25519.GetPublicKey(seed3);

        // Concatenate keys into a single continuous buffer (authorized_keys model)
        byte[] authorizedKeys = pubKey1.Concat(pubKey2).Concat(pubKey3).ToArray();

        // Sign with key2
        byte[] signature = Kassyi.GSharp.Cryptography.Ed25519.Sign(RealNvcc_DummyFatbin, seed2);
        byte[] signedBinary = RealNvcc_DummyFatbin.Concat(signature).ToArray();

        bool success = FatbinParser.TryParse(signedBinary, out FatbinImage? image);
        Assert.True(success);
        Assert.NotNull(image);

        // Verify against whitelist
        Assert.True(image.VerifySignature(authorizedKeys));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GpuSecurityPolicy_EmbeddedKeys_IsNotEmpty()
    {
        ReadOnlySpan<byte> embedded = GpuSecurityPolicy.EmbeddedKeys;
        Assert.False(embedded.IsEmpty);
        Assert.True(embedded.Length >= 32);
        Assert.True(embedded.Length % 32 == 0);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GpuSecurityPolicy_DynamicKeyRegistration_VerifiesImage()
    {
        byte[] dynamicSeed = new byte[32];
        Array.Fill(dynamicSeed, (byte)0x77);
        byte[] dynamicPubKey = Kassyi.GSharp.Cryptography.Ed25519.GetPublicKey(dynamicSeed);

        // Sign dummy fatbin with dynamic key
        byte[] signature = Kassyi.GSharp.Cryptography.Ed25519.Sign(RealNvcc_DummyFatbin, dynamicSeed);
        byte[] signedBinary = RealNvcc_DummyFatbin.Concat(signature).ToArray();

        bool success = FatbinParser.TryParse(signedBinary, out FatbinImage? image);
        Assert.True(success);
        Assert.NotNull(image);

        // Register key dynamically
        GpuSecurityPolicy.RegisterTrustedKey(dynamicPubKey);

        // After registration, VerifyImage must succeed
        Assert.True(GpuSecurityPolicy.VerifyImage(image));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GpuSecurityPolicy_VerifyImageWithUntrustedKey_ReturnsFalse()
    {
        byte[] unknownSeed = new byte[32];
        Array.Fill(unknownSeed, (byte)0xEE);

        byte[] signature = Kassyi.GSharp.Cryptography.Ed25519.Sign(RealNvcc_DummyFatbin, unknownSeed);
        byte[] signedBinary = RealNvcc_DummyFatbin.Concat(signature).ToArray();

        bool success = FatbinParser.TryParse(signedBinary, out FatbinImage? image);
        Assert.True(success);
        Assert.NotNull(image);

        // An unknown key must not be verified by default
        Assert.False(GpuSecurityPolicy.VerifyImage(image));
    }
}
