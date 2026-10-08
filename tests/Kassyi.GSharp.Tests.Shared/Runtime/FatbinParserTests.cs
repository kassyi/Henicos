#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Kassyi.GSharp.Runtime.Fatbin;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class FatbinParserTests
{
    // Real NVCC/fatbinary compiled outputs (CUDA 13.4, Sm_80, Sm_86)
    private static readonly byte[] RealNvcc_DummyFatbin = new byte[]
    {
        0x50, 0xed, 0x55, 0xba, 0x01, 0x00, 0x10, 0x00, 0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x01, 0x50, 0x00, 0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x6a, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x50, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x41, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x76, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x48, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x28, 0xb5, 0x2f, 0xfd, 0x20, 0x76, 0x0d, 0x03, 0x00, 0x42, 0x86, 0x15, 0x1a, 0x80, 0xb9, 0x39,
        0xc0, 0xe1, 0x6d, 0x09, 0x11, 0x06, 0x2d, 0x62, 0xe4, 0xcd, 0xcd, 0x32, 0x81, 0xbd, 0xb5, 0xd9,
        0x93, 0x13, 0xd0, 0x5c, 0x88, 0xee, 0x07, 0x80, 0x5b, 0xdc, 0xa0, 0x5d, 0x4f, 0x81, 0x02, 0x93,
        0x79, 0x8c, 0xc4, 0xb8, 0x90, 0x22, 0x84, 0x03, 0xc8, 0x99, 0xde, 0x90, 0xcb, 0x5a, 0x49, 0xc2,
        0x45, 0x1b, 0xbb, 0xf1, 0x08, 0x41, 0xad, 0x52, 0x7c, 0x77, 0x0c, 0x7c, 0x42, 0x6d, 0xee, 0x5e,
        0x53, 0x42, 0x3e, 0x73, 0xe4, 0x5a, 0x6c, 0x2f, 0x28, 0x6a, 0x3e, 0xe3, 0x03, 0xd9, 0x41, 0xad,
        0x5e, 0x71, 0x02, 0x00, 0x3b, 0x05, 0xa9, 0xf8, 0x98, 0x66, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    private static readonly byte[] RealNvcc_MultiFatbin = new byte[]
    {
        0x50, 0xed, 0x55, 0xba, 0x01, 0x00, 0x10, 0x00, 0x80, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x01, 0x50, 0x00, 0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x6a, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x50, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x41, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x76, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x48, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x28, 0xb5, 0x2f, 0xfd, 0x20, 0x76, 0x0d, 0x03, 0x00, 0x42, 0x86, 0x15, 0x1a, 0x80, 0xb9, 0x39,
        0xc0, 0xe1, 0x6d, 0x09, 0x11, 0x06, 0x2d, 0x62, 0xe4, 0xcd, 0xcd, 0x32, 0x81, 0xbd, 0xb5, 0xd9,
        0x93, 0x13, 0xd0, 0x5c, 0x88, 0xee, 0x07, 0x80, 0x5b, 0xdc, 0xa0, 0x5d, 0x4f, 0x81, 0x02, 0x93,
        0x79, 0x8c, 0xc4, 0xb8, 0x90, 0x22, 0x84, 0x03, 0xc8, 0x99, 0xde, 0x90, 0xcb, 0x5a, 0x49, 0xc2,
        0x45, 0x1b, 0xbb, 0xf1, 0x08, 0x41, 0xad, 0x52, 0x7c, 0x77, 0x0c, 0x7c, 0x42, 0x6d, 0xee, 0x5e,
        0x53, 0x42, 0x3e, 0x73, 0xe4, 0x5a, 0x6c, 0x2f, 0x28, 0x6a, 0x3e, 0xe3, 0x03, 0xd9, 0x41, 0xad,
        0x5e, 0x71, 0x02, 0x00, 0x3b, 0x05, 0xa9, 0xf8, 0x98, 0x66, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x01, 0x50, 0x00, 0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x6a, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x56, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x41, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x76, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x48, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x28, 0xb5, 0x2f, 0xfd, 0x20, 0x76, 0x0d, 0x03, 0x00, 0x42, 0x86, 0x15, 0x1a, 0x80, 0xb9, 0x39,
        0xc0, 0xe1, 0x6d, 0x09, 0x11, 0x06, 0x2d, 0x62, 0xe4, 0xcd, 0xcd, 0x32, 0x81, 0xbd, 0xb5, 0xd9,
        0x93, 0x13, 0xd0, 0x5c, 0x88, 0xee, 0x07, 0x80, 0x5b, 0xdc, 0xa0, 0x5d, 0x4f, 0x81, 0x02, 0x93,
        0x79, 0x8c, 0xc4, 0xb8, 0x90, 0x22, 0x84, 0x03, 0xc8, 0x99, 0xde, 0x90, 0xcb, 0x5a, 0x49, 0xc2,
        0x45, 0x1b, 0xbb, 0xf1, 0x08, 0x41, 0xad, 0x52, 0x7c, 0x77, 0x0c, 0x7c, 0x42, 0x6d, 0xee, 0x5e,
        0x53, 0x42, 0x3e, 0x73, 0xe4, 0x5a, 0x6c, 0x2f, 0x28, 0x6a, 0x3e, 0xe3, 0x03, 0xd9, 0x41, 0xad,
        0x5e, 0x71, 0x02, 0x00, 0x3b, 0x05, 0xa9, 0xf8, 0x98, 0x66, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    private static readonly byte[] RealNvcc_IdentFatbin = new byte[]
    {
        0x50, 0xed, 0x55, 0xba, 0x01, 0x00, 0x10, 0x00, 0xd0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x01, 0x60, 0x00, 0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x6a, 0x00, 0x00, 0x00, 0x50, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x50, 0x00, 0x00, 0x00,
        0x40, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00, 0x41, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x76, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x6d, 0x79, 0x5f, 0x6b, 0x65, 0x72, 0x6e, 0x65, 0x6c, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x58, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x28, 0xb5, 0x2f, 0xfd, 0x20, 0x76, 0x0d, 0x03, 0x00, 0x42, 0x86, 0x15, 0x1a, 0x80, 0xb9, 0x39,
        0xc0, 0xe1, 0x6d, 0x09, 0x11, 0x06, 0x2d, 0x62, 0xe4, 0xcd, 0xcd, 0x32, 0x81, 0xbd, 0xb5, 0xd9,
        0x93, 0x13, 0xd0, 0x5c, 0x88, 0xee, 0x07, 0x80, 0x5b, 0xdc, 0xa0, 0x5d, 0x4f, 0x81, 0x02, 0x93,
        0x79, 0x8c, 0xc4, 0xb8, 0x90, 0x22, 0x84, 0x03, 0xc8, 0x99, 0xde, 0x90, 0xcb, 0x5a, 0x49, 0xc2,
        0x45, 0x1b, 0xbb, 0xf1, 0x08, 0x41, 0xad, 0x52, 0x7c, 0x77, 0x0c, 0x7c, 0x42, 0x6d, 0xee, 0x5e,
        0x53, 0x42, 0x3e, 0x73, 0xe4, 0x5a, 0x6c, 0x2f, 0x28, 0x6a, 0x3e, 0xe3, 0x03, 0xd9, 0x41, 0xad,
        0x5e, 0x71, 0x02, 0x00, 0x3b, 0x05, 0xa9, 0xf8, 0x98, 0x66, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GSharpFatbinExtension_Constant()
    {
        Assert.Equal(".gfatbin", FatbinParser.GSharpFatbinExtension);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryParse_RealNvcc_DummyFatbin_HappyPath()
    {
        bool success = FatbinParser.TryParse(RealNvcc_DummyFatbin, out FatbinImage? image);

        Assert.True(success);
        Assert.NotNull(image);
        Assert.Equal(0xBA55ED50u, image.Magic);
        Assert.Equal(1, image.Version);
        Assert.Equal(208ul, image.PayloadSize);
        Assert.False(image.HasGSharpSignature);
        Assert.True(image.Signature.IsEmpty);
        Assert.Equal(208, image.Payload.Length);

        Assert.Single(image.Items);
        FatbinItemInfo item = image.Items[0];
        Assert.Equal(FatbinItemInfo.TypePtx, item.Type);
        Assert.Equal(80u, item.ComputeVersion);
        Assert.Equal((ushort)8, item.Major);
        Assert.Equal((ushort)0, item.Minor);
        Assert.Equal(112ul, item.DataSize);
        Assert.Null(item.ObjectName);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryParse_RealNvcc_MultiFatbin_HappyPath()
    {
        bool success = FatbinParser.TryParse(RealNvcc_MultiFatbin, out FatbinImage? image);

        Assert.True(success);
        Assert.NotNull(image);
        Assert.Equal(2, image.Items.Count);

        FatbinItemInfo item0 = image.Items[0];
        Assert.Equal(80u, item0.ComputeVersion);
        Assert.Equal((ushort)8, item0.Major);
        Assert.Equal((ushort)0, item0.Minor);

        FatbinItemInfo item1 = image.Items[1];
        Assert.Equal(86u, item1.ComputeVersion);
        Assert.Equal((ushort)8, item1.Major);
        Assert.Equal((ushort)6, item1.Minor);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryParse_RealNvcc_IdentFatbin_HappyPath()
    {
        bool success = FatbinParser.TryParse(RealNvcc_IdentFatbin, out FatbinImage? image);

        Assert.True(success);
        Assert.NotNull(image);
        Assert.Single(image.Items);
        Assert.Equal("my_kernel", image.Items[0].ObjectName);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Ed25519_Rfc8032_TestVector()
    {
        // RFC 8032 Section 7.1 Test Vector 1 (Empty message)
        byte[] secretKeySeed = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] expectedPublicKey = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        byte[] expectedSignature = Convert.FromHexString("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");

        byte[] actualPublicKey = Kassyi.GSharp.Cryptography.Ed25519.GetPublicKey(secretKeySeed);
        Assert.Equal(expectedPublicKey, actualPublicKey);

        byte[] actualSignature = Kassyi.GSharp.Cryptography.Ed25519.Sign(ReadOnlySpan<byte>.Empty, secretKeySeed);
        Assert.Equal(expectedSignature, actualSignature);

        bool isValid = Kassyi.GSharp.Cryptography.Ed25519.Verify(ReadOnlySpan<byte>.Empty, actualSignature, actualPublicKey);
        Assert.True(isValid);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryParse_WithGSharpSignature_Ed25519_HappyPath()
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
    public void Test_VerifySignature_CorruptedPayload_Fails()
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
    public void Test_VerifySignature_WrongPublicKey_Fails()
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
    public void Test_VerifySignature_AuthorizedKeysWhitelist_HappyPath()
    {
        byte[] seed1 = new byte[32]; Array.Fill(seed1, (byte)0x11);
        byte[] seed2 = new byte[32]; Array.Fill(seed2, (byte)0x22);
        byte[] seed3 = new byte[32]; Array.Fill(seed3, (byte)0x33);

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
    public void Test_GpuSecurityPolicy_EmbeddedKeys_NotEmpty()
    {
        ReadOnlySpan<byte> embedded = GpuSecurityPolicy.EmbeddedKeys;
        Assert.False(embedded.IsEmpty);
        Assert.True(embedded.Length >= 32);
        Assert.True(embedded.Length % 32 == 0);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GpuSecurityPolicy_DynamicKeyRegistration_And_VerifyImage()
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

        // Before registration, if dynamic key is not in embedded, verify may fail (or check specifically for unknown key)
        // Register key dynamically
        GpuSecurityPolicy.RegisterTrustedKey(dynamicPubKey);

        // After registration, VerifyImage must succeed
        Assert.True(GpuSecurityPolicy.VerifyImage(image));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_GpuSecurityPolicy_VerifyImage_UntrustedKey_Fails()
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

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(8, 0, true)]   // Exact match for sm_80 PTX
    [InlineData(8, 6, true)]   // Forward compatible (SM 8.6 can run sm_80 PTX)
    [InlineData(9, 0, true)]   // Forward compatible (SM 9.0 can run sm_80 PTX)
    [InlineData(7, 5, false)]  // Older architecture cannot run sm_80 PTX
    [InlineData(6, 1, false)]  // Older architecture cannot run sm_80 PTX
    public void Test_IsCompatibleWith_PtxForwardCompatibility(int deviceMajor, int deviceMinor, bool expected)
    {
        bool success = FatbinParser.TryParse(RealNvcc_DummyFatbin, out FatbinImage? image);
        Assert.True(success);
        Assert.NotNull(image);

        bool compatible = image.IsCompatibleWith(deviceMajor, deviceMinor);
        Assert.Equal(expected, compatible);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_IsCompatibleWith_CubinExactMatch()
    {
        // Synthesize a CUBIN item (Type = 2) for sm_80
        byte[] cubinItem = CreateMockFatbin(type: FatbinItemInfo.TypeCubin, computeVersion: 80, major: 8, minor: 0);
        bool success = FatbinParser.TryParse(cubinItem, out FatbinImage? image);
        Assert.True(success);
        Assert.NotNull(image);

        // Native CUBIN requires exact architecture match
        Assert.True(image.IsCompatibleWith(8, 0));
        Assert.False(image.IsCompatibleWith(8, 6)); // Incompatible for native cubin
        Assert.False(image.IsCompatibleWith(9, 0)); // Incompatible for native cubin
        Assert.False(image.IsCompatibleWith(7, 5));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TryParse_SadPath_InvalidMagic()
    {
        byte[] invalidMagic = (byte[])RealNvcc_DummyFatbin.Clone();
        invalidMagic[0] = 0x00;
        invalidMagic[1] = 0x00;
        invalidMagic[2] = 0x00;
        invalidMagic[3] = 0x00;

        bool success = FatbinParser.TryParse(invalidMagic, out FatbinImage? image);

        Assert.False(success);
        Assert.Null(image);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    public void Test_TryParse_Boundary_TooShort(int length)
    {
        byte[] shortArray = new byte[length];
        bool success = FatbinParser.TryParse(shortArray, out FatbinImage? image);

        Assert.False(success);
        Assert.Null(image);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TryParse_Boundary_TrailingBytesNot64()
    {
        // 32 extra bytes instead of 64 bytes signature -> HasGSharpSignature should be false
        byte[] extraBytes = RealNvcc_DummyFatbin.Concat(new byte[32]).ToArray();

        bool success = FatbinParser.TryParse(extraBytes, out FatbinImage? image);

        // Fatbin is still valid, but signature is NOT recognized because Length != PayloadSize + 64
        Assert.True(success);
        Assert.NotNull(image);
        Assert.False(image.HasGSharpSignature);
        Assert.True(image.Signature.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TryParse_SadPath_CorruptedPayloadSize()
    {
        byte[] corrupted = (byte[])RealNvcc_DummyFatbin.Clone();
        // Corrupt Payload size (at offset 8) to be 1,000,000 bytes
        BitConverter.TryWriteBytes(corrupted.AsSpan(8, 8), 1_000_000UL);

        bool success = FatbinParser.TryParse(corrupted, out FatbinImage? image);

        Assert.False(success);
        Assert.Null(image);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TryParse_SadPath_CorruptedItemHeader()
    {
        byte[] corrupted = (byte[])RealNvcc_DummyFatbin.Clone();
        // Item header size at offset 16 + 4 = 20 set to 0
        BitConverter.TryWriteBytes(corrupted.AsSpan(20, 4), 0u);

        bool success = FatbinParser.TryParse(corrupted, out FatbinImage? image);

        Assert.False(success);
        Assert.Null(image);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryParse_ReadOnlyMemory_And_Span_Overloads()
    {
        ReadOnlyMemory<byte> memory = RealNvcc_DummyFatbin;
        ReadOnlySpan<byte> span = RealNvcc_DummyFatbin.AsSpan();

        bool successMem = FatbinParser.TryParse(memory, out FatbinImage? imageMem);
        bool successSpan = FatbinParser.TryParse(span, out FatbinImage? imageSpan);

        Assert.True(successMem);
        Assert.True(successSpan);
        Assert.NotNull(imageMem);
        Assert.NotNull(imageSpan);
        Assert.Equal(imageMem.PayloadSize, imageSpan.PayloadSize);
        Assert.Equal(imageMem.Items.Count, imageSpan.Items.Count);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(8, 0, 8, 0, true)]  // Exact match
    [InlineData(8, 0, 8, 6, true)]  // Newer minor
    [InlineData(8, 0, 9, 0, true)]  // Newer major
    [InlineData(8, 6, 8, 0, false)] // Target older minor
    [InlineData(8, 0, 7, 5, false)] // Target older major
    public void Test_IsCompatibleWith_Ptx_ForwardCompatibility(ushort ptxMajor, ushort ptxMinor, int deviceMajor, int deviceMinor, bool expected)
    {
        byte[] fatbinBytes = CreateMockFatbin(FatbinItemInfo.TypePtx, (uint)(ptxMajor * 10 + ptxMinor), ptxMajor, ptxMinor);
        bool parsed = FatbinParser.TryParse(fatbinBytes, out FatbinImage? image);

        Assert.True(parsed);
        Assert.NotNull(image);
        Assert.Equal(expected, image.IsCompatibleWith(deviceMajor, deviceMinor));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(8, 0, 8, 0, true)]  // Exact match
    [InlineData(8, 0, 8, 6, false)] // Different minor
    [InlineData(8, 0, 9, 0, false)] // Newer major (CUBIN is native machine code, not forward compatible)
    [InlineData(8, 6, 8, 0, false)] // Older minor
    [InlineData(8, 0, 7, 5, false)] // Older major
    public void Test_IsCompatibleWith_Cubin_ExactMatchOnly(ushort cubinMajor, ushort cubinMinor, int deviceMajor, int deviceMinor, bool expected)
    {
        byte[] fatbinBytes = CreateMockFatbin(FatbinItemInfo.TypeCubin, (uint)(cubinMajor * 10 + cubinMinor), cubinMajor, cubinMinor);
        bool parsed = FatbinParser.TryParse(fatbinBytes, out FatbinImage? image);

        Assert.True(parsed);
        Assert.NotNull(image);
        Assert.Equal(expected, image.IsCompatibleWith(deviceMajor, deviceMinor));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1, 0)]
    [InlineData(8, -1)]
    [InlineData(-1, -1)]
    public void Test_IsCompatibleWith_NegativeArchitecture_ReturnsFalse(int deviceMajor, int deviceMinor)
    {
        byte[] fatbinBytes = CreateMockFatbin(FatbinItemInfo.TypePtx, 80, 8, 0);
        bool parsed = FatbinParser.TryParse(fatbinBytes, out FatbinImage? image);

        Assert.True(parsed);
        Assert.NotNull(image);
        Assert.False(image.IsCompatibleWith(deviceMajor, deviceMinor));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_IsCompatibleWith_MultipleItems_MatchesAny()
    {
        // FATBIN containing sm_86 CUBIN and sm_80 PTX
        byte[] fatbinBytes = CreateMockFatbin(
            (FatbinItemInfo.TypeCubin, 86u, (ushort)8, (ushort)6),
            (FatbinItemInfo.TypePtx, 80u, (ushort)8, (ushort)0)
        );

        bool parsed = FatbinParser.TryParse(fatbinBytes, out FatbinImage? image);
        Assert.True(parsed);
        Assert.NotNull(image);

        // On sm_86: Matches CUBIN (and PTX)
        Assert.True(image.IsCompatibleWith(8, 6));

        // On sm_80: Matches PTX
        Assert.True(image.IsCompatibleWith(8, 0));

        // On sm_90: Matches PTX forward compatibility
        Assert.True(image.IsCompatibleWith(9, 0));

        // On sm_75: Neither matches
        Assert.False(image.IsCompatibleWith(7, 5));
    }

    internal static byte[] CreateMockFatbin(ushort type, uint computeVersion, ushort major, ushort minor)
    {
        return CreateMockFatbin([(type, computeVersion, major, minor)]);
    }

    internal static byte[] CreateMockFatbin(params (ushort type, uint computeVersion, ushort major, ushort minor)[] items)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        ulong payloadSize = (ulong)(items.Length * (80 + 16));

        // fat_elf_header (16 bytes)
        bw.Write(0xBA55ED50u); // magic
        bw.Write((ushort)1);   // version
        bw.Write((ushort)16);  // header_size
        bw.Write(payloadSize); // payload items size

        foreach (var item in items)
        {
            // fat_elf_item (80 bytes header + 16 bytes dummy data)
            bw.Write(item.type);           // 0x00: type
            bw.Write((byte)1);             // 0x02: param_2
            bw.Write((byte)1);             // 0x03: param_3
            bw.Write(80u);                 // 0x04: header_size
            bw.Write(16UL);                // 0x08: size
            bw.Write(16u);                 // 0x10: compressed_size
            bw.Write(0u);                  // 0x14: param_4
            bw.Write(item.minor);          // 0x18: minor
            bw.Write(item.major);          // 0x1A: major
            bw.Write(item.computeVersion); // 0x1C: compute_version
            bw.Write(0u);                  // 0x20: name_off
            bw.Write(0u);                  // 0x24: name_len
            bw.Write(0UL);                 // 0x28: flags
            bw.Write(0UL);                 // 0x30: zero
            bw.Write(16UL);                // 0x38: uncompressed_size
            bw.Write(new byte[16]);        // 0x40 - 0x50: padding

            // Item payload (16 bytes)
            bw.Write(new byte[16]);
        }

        return ms.ToArray();
    }
}
