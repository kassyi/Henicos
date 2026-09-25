using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// AOT binary signature validation logic.
/// </summary>
internal static class AotSignatureValidator
{
    public readonly struct Hash256 : IEquatable<Hash256>
    {
        private readonly ulong _a;
        private readonly ulong _b;
        private readonly ulong _c;
        private readonly ulong _d;

        public Hash256(ReadOnlySpan<byte> span)
        {
            if (span.Length < 32)
            {
                throw new ArgumentException("Span too short for Hash256", nameof(span));
            }

            _a = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(0, 8));
            _b = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(8, 8));
            _c = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(16, 8));
            _d = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(24, 8));
        }

        public bool Equals(Hash256 other) => _a == other._a && _b == other._b && _c == other._c && _d == other._d;

        public override bool Equals(object? obj) => obj is Hash256 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);
    }

    /// <summary>
    /// Extracts the 32-byte embedded SHA256 hash from the end of the binary, 
    /// validates it against the actual hash of the payload, and returns the payload and the hash.
    /// </summary>
    public static (byte[] Payload, Hash256 EmbeddedHash) ExtractAndVerify(ReadOnlySpan<byte> binaryData)
    {
        if (binaryData.Length < 32)
        {
            throw new InvalidOperationException("AOT native binary is too small to contain a valid hash.");
        }

        var embeddedHashBytes = binaryData.Slice(binaryData.Length - 32);
        var expectedHashStruct = new Hash256(embeddedHashBytes);
        var expectedHashString = Convert.ToHexStringLower(embeddedHashBytes);

        var payload = binaryData.Slice(0, binaryData.Length - 32).ToArray();
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(payload));

        if (!string.Equals(actualHash, expectedHashString, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"AOT native binary hash mismatch. Expected: {expectedHashString}");
        }

        return (payload, expectedHashStruct);
    }
}
