
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

        lock (s_lock)
        {
            var next = new byte[s_dynamicKeys.Length + Ed25519.PublicKeySize];
            Buffer.BlockCopy(s_dynamicKeys, 0, next, 0, s_dynamicKeys.Length);
            publicKey.CopyTo(next.AsSpan(s_dynamicKeys.Length));
            s_dynamicKeys = next;
        }
    }

    /// <summary>
    /// Verifies whether the specified FATBIN image has a valid digital signature matching any embedded or dynamic trusted key.
    /// </summary>
    /// <param name="image">The parsed FATBIN image to verify.</param>
    /// <returns><see langword="true"/> if the signature matches an authorized trusted key; otherwise, <see langword="false"/>.</returns>
    public static bool VerifyImage(FatbinImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var embedded = EmbeddedKeys;
        if (!embedded.IsEmpty && image.VerifySignature(embedded))
        {
            return true;
        }

        byte[] dynamicCopy;
        lock (s_lock)
        {
            dynamicCopy = s_dynamicKeys;
        }

        return dynamicCopy.Length > 0 && image.VerifySignature(dynamicCopy);
    }
}
