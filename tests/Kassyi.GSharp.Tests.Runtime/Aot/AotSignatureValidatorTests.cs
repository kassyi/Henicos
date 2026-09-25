using System.Security.Cryptography;
using FluentAssertions;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Runtime.Aot;

public class AotSignatureValidatorTests
{
    [Fact]
    public void ExtractAndVerify_WhenBinaryIsTooSmall_ThrowsInvalidOperationException()
    {
        // Arrange
        var binary = new byte[31]; // Less than 32 bytes

        // Act
        var act = () => AotSignatureValidator.ExtractAndVerify(binary);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("AOT native binary is too small to contain a valid hash.");
    }

    [Fact]
    public void ExtractAndVerify_WhenHashMismatches_ThrowsInvalidOperationException()
    {
        // Arrange
        var payload = "InvalidData"u8.ToArray();
        var binary = new byte[payload.Length + 32];
        Array.Copy(payload, binary, payload.Length);

        // Fill hash part with zeroes (which will not match the payload hash)
        // Act
        var act = () => AotSignatureValidator.ExtractAndVerify(binary);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("AOT native binary hash mismatch*");
    }

    [Fact]
    public void ExtractAndVerify_WhenHashMatches_ReturnsPayloadAndStruct()
    {
        // Arrange
        var payload = "ValidAotBinaryData"u8.ToArray();
        var hash = SHA256.HashData(payload);
        
        var binary = new byte[payload.Length + 32];
        Array.Copy(payload, binary, payload.Length);
        Array.Copy(hash, 0, binary, payload.Length, 32);

        // Act
        var result = AotSignatureValidator.ExtractAndVerify(binary);

        // Assert
        result.Payload.Should().BeEquivalentTo(payload);
        
        // Ensure struct was correctly constructed from the hash part
        var expectedHashStruct = new AotSignatureValidator.Hash256(hash);
        result.EmbeddedHash.Should().Be(expectedHashStruct);
    }
}
