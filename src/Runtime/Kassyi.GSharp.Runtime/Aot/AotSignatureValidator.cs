using System.Buffers.Binary;
using Kassyi.GSharp.Runtime.Fatbin;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// AOT binary digital signature validation logic using Ed25519 and pinned public keys.
/// </summary>
internal static class AotSignatureValidator
{
    public const int SignatureSize = Ed25519.SignatureSize;

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
    /// Extracts the 64-byte embedded Ed25519 digital signature from the end of the binary,
    /// verifies it against pinned trusted public keys, and returns the verified payload and hash.
    /// </summary>
    public static (byte[] Payload, Hash256 EmbeddedHash) ExtractAndVerify(ReadOnlySpan<byte> binaryData)
    {
        if (binaryData.Length < SignatureSize)
        {
            throw new InvalidOperationException("AOT native binary is too small to contain a valid signature.");
        }

        var signature = binaryData.Slice(binaryData.Length - SignatureSize);
        var payloadSpan = binaryData.Slice(0, binaryData.Length - SignatureSize);

        var trustedKeys = GpuSecurityPolicy.EmbeddedKeys;
        var verified = false;

        if (!trustedKeys.IsEmpty)
        {
            for (var offset = 0; offset < trustedKeys.Length; offset += Ed25519.PublicKeySize)
            {
                var key = trustedKeys.Slice(offset, Ed25519.PublicKeySize);
                if (Ed25519.Verify(payloadSpan, signature, key))
                {
                    verified = true;
                    break;
                }
            }
        }

        if (!verified)
        {
            throw new InvalidOperationException("AOT native binary signature verification failed: signature does not match any trusted public key.");
        }

        var payload = payloadSpan.ToArray();
        var expectedHashStruct = new Hash256(signature.Slice(0, 32));

        return (payload, expectedHashStruct);
    }
}
