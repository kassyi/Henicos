namespace Kassyi.GSharp.Runtime.Fatbin;

/// <summary>
/// Manages trusted Ed25519 public keys and signature verification policies for GPU binaries.
/// </summary>
public static partial class GpuSecurityPolicy
{
    private static readonly Lock s_lock = new();
    private static byte[] s_dynamicKeys = [];

    /// <summary>
    /// Gets the embedded trusted Ed25519 public keys (concatenated 32-byte keys).
    /// </summary>
    public static ReadOnlySpan<byte> EmbeddedKeys => GetEmbeddedKeys();

    static partial void GetCustomEmbeddedKeys(ref ReadOnlySpan<byte> keys);

    private static ReadOnlySpan<byte> GetEmbeddedKeys()
    {
        ReadOnlySpan<byte> customKeys = default;
        GetCustomEmbeddedKeys(ref customKeys);
        return !customKeys.IsEmpty ? customKeys : Ed25519.DefaultDevPublicKey;
    }

    /// <summary>
    /// Registers a trusted 32-byte Ed25519 public key dynamically at runtime.
    /// </summary>
    /// <param name="publicKey">The 32-byte public key.</param>
    public static void RegisterTrustedKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != Ed25519.PublicKeySize)
        {
            throw new ArgumentException($"Public key must be {Ed25519.PublicKeySize} bytes.", nameof(publicKey));
        }

        if (ContainsKey(EmbeddedKeys, publicKey))
        {
            return;
        }

        lock (s_lock)
        {
            if (ContainsKey(s_dynamicKeys, publicKey))
            {
                return;
            }

            var next = new byte[s_dynamicKeys.Length + Ed25519.PublicKeySize];
            Buffer.BlockCopy(s_dynamicKeys, 0, next, 0, s_dynamicKeys.Length);
            publicKey.CopyTo(next.AsSpan(s_dynamicKeys.Length));
            s_dynamicKeys = next;
        }
    }

    /// <summary>
    /// Verifies an Ed25519 signature against a contiguous buffer of 32-byte public keys.
    /// </summary>
    /// <param name="payload">The binary payload to verify.</param>
    /// <param name="signature">The 64-byte Ed25519 signature.</param>
    /// <param name="trustedKeys">A contiguous byte span containing one or more 32-byte public keys.</param>
    /// <returns><see langword="true"/> if the signature matches any trusted key; otherwise, <see langword="false"/>.</returns>
    public static bool VerifySignature(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> trustedKeys)
    {
        if (signature.Length != Ed25519.SignatureSize || trustedKeys.IsEmpty || (trustedKeys.Length % Ed25519.PublicKeySize) != 0)
        {
            return false;
        }

        for (var offset = 0; offset < trustedKeys.Length; offset += Ed25519.PublicKeySize)
        {
            var key = trustedKeys.Slice(offset, Ed25519.PublicKeySize);
            if (Ed25519.Verify(payload, signature, key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Verifies an Ed25519 signature against any embedded or dynamically registered trusted key.
    /// </summary>
    /// <param name="payload">The binary payload to verify.</param>
    /// <param name="signature">The 64-byte Ed25519 signature.</param>
    /// <returns><see langword="true"/> if the signature matches an authorized trusted key; otherwise, <see langword="false"/>.</returns>
    public static bool VerifySignature(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        var embedded = EmbeddedKeys;
        if (!embedded.IsEmpty && VerifySignature(payload, signature, embedded))
        {
            return true;
        }

        byte[] dynamicCopy;
        lock (s_lock)
        {
            dynamicCopy = s_dynamicKeys;
        }

        return dynamicCopy.Length > 0 && VerifySignature(payload, signature, dynamicCopy);
    }

    /// <summary>
    /// Verifies whether the specified FATBIN image has a valid digital signature matching any embedded or dynamic trusted key.
    /// </summary>
    /// <param name="image">The parsed FATBIN image to verify.</param>
    /// <returns><see langword="true"/> if the signature matches an authorized trusted key; otherwise, <see langword="false"/>.</returns>
    public static bool VerifyImage(FatbinImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.HasGSharpSignature && VerifySignature(image.Payload.Span, image.Signature.Span);
    }

    private static bool ContainsKey(ReadOnlySpan<byte> keyBuffer, ReadOnlySpan<byte> publicKey)
    {
        for (var offset = 0; offset + Ed25519.PublicKeySize <= keyBuffer.Length; offset += Ed25519.PublicKeySize)
        {
            if (keyBuffer.Slice(offset, Ed25519.PublicKeySize).SequenceEqual(publicKey))
            {
                return true;
            }
        }

        return false;
    }
}
