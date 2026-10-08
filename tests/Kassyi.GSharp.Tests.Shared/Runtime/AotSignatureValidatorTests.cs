#nullable enable

using System;
using Kassyi.GSharp.Cryptography;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class AotSignatureValidatorTests
{
    [Fact]
    [Trait("Category", "Boundary")]
    public void ExtractAndVerify_WhenBinaryIsTooSmall_ThrowsInvalidOperationException()
    {
        // Arrange
        var binary = new byte[63]; // Less than 64 bytes

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => AotSignatureValidator.ExtractAndVerify(binary));
        Assert.Contains("AOT native binary is too small to contain a valid signature", ex.Message);
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public void ExtractAndVerify_WhenSignatureMismatches_ThrowsInvalidOperationException()
    {
        // Arrange
        var payload = "InvalidData"u8.ToArray();
        var binary = new byte[payload.Length + Ed25519.SignatureSize];
        Array.Copy(payload, binary, payload.Length);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => AotSignatureValidator.ExtractAndVerify(binary));
        Assert.Contains("AOT native binary signature verification failed", ex.Message);
    }

    [Fact]
    [Trait("Category", "HappyPath")]
    public void ExtractAndVerify_WhenSignatureMatches_ReturnsPayloadAndStruct()
    {
        // Arrange
        var payload = "ValidAotBinaryData"u8.ToArray();
        var signature = Ed25519.Sign(payload, Ed25519.DefaultDevKeySeed);

        var binary = new byte[payload.Length + Ed25519.SignatureSize];
        Array.Copy(payload, binary, payload.Length);
        Array.Copy(signature, 0, binary, payload.Length, Ed25519.SignatureSize);

        // Act
        var result = AotSignatureValidator.ExtractAndVerify(binary);

        // Assert
        Assert.Equal(payload, result.Payload);

        // Ensure struct was correctly constructed from the signature part
        var expectedHashStruct = new AotSignatureValidator.Hash256(signature.AsSpan(0, 32));
        Assert.Equal(expectedHashStruct, result.EmbeddedHash);
    }
}
