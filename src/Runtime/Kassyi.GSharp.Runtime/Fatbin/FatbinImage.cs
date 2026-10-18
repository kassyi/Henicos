namespace Kassyi.GSharp.Runtime.Fatbin;

/// <summary>
/// Represents a parsed NVIDIA FATBIN binary image and its embedded targets.
/// </summary>
/// <param name="magic">The FATBIN magic number.</param>
/// <param name="version">The FATBIN format version.</param>
/// <param name="payloadSize">The total payload size in bytes.</param>
/// <param name="hasGSharpSignature">Whether a G# 64-byte Ed25519 signature is present.</param>
/// <param name="payload">The binary payload data slice.</param>
/// <param name="signature">The appended signature slice, if present.</param>
/// <param name="items">The collection of parsed embedded modules.</param>
public sealed class FatbinImage(
    uint magic,
    ushort version,
    ulong payloadSize,
    bool hasGSharpSignature,
    ReadOnlyMemory<byte> payload,
    ReadOnlyMemory<byte> signature,
    IReadOnlyList<FatbinItemInfo> items)
{
    /// <summary>
    /// Gets the FATBIN magic number (expected to be <c>0xBA55ED50</c>).
    /// </summary>
    public uint Magic => magic;

    /// <summary>
    /// Gets the FATBIN format version.
    /// </summary>
    public ushort Version => version;

    /// <summary>
    /// Gets the total size of the FATBIN binary payload in bytes (including headers, excluding appended signatures).
    /// </summary>
    public ulong PayloadSize => payloadSize;

    /// <summary>
    /// Gets a value indicating whether a G# 64-byte Ed25519 digital signature is detected at the tail.
    /// Determined strictly by whether the source binary length equals <see cref="PayloadSize"/> + 64.
    /// </summary>
    public bool HasGSharpSignature => hasGSharpSignature;

    /// <summary>
    /// Gets the raw FATBIN binary payload (excluding signature).
    /// </summary>
    public ReadOnlyMemory<byte> Payload => payload;

    /// <summary>
    /// Gets the appended 64-byte G# Ed25519 digital signature, or an empty memory slice if not signed.
    /// </summary>
    public ReadOnlyMemory<byte> Signature => signature;

    /// <summary>
    /// Gets the metadata of all embedded modules (PTX or cuBIN) contained in this fatbin.
    /// </summary>
    public IReadOnlyList<FatbinItemInfo> Items => items;

    /// <summary>
    /// Verifies if any embedded module within this FATBIN image is compatible with the specified GPU compute capability.
    /// </summary>
    /// <param name="deviceMajor">The major compute capability version of the physical GPU (e.g. 8 for sm_80).</param>
    /// <param name="deviceMinor">The minor compute capability version of the physical GPU (e.g. 0 for sm_80, 6 for sm_86).</param>
    /// <returns><see langword="true"/> if at least one module can execute on the target architecture; otherwise, <see langword="false"/>.</returns>
    public bool IsCompatibleWith(int deviceMajor, int deviceMinor)
    {
        if (deviceMajor < 0 || deviceMinor < 0)
        {
            return false;
        }

        foreach (var item in Items)
        {
            if (item.IsCompatibleWith(deviceMajor, deviceMinor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Verifies the appended Ed25519 signature against one or more trusted 32-byte public keys (Authorized Keys model).
    /// </summary>
    /// <param name="trustedKeys">A contiguous byte span containing one or more 32-byte public keys.</param>
    /// <returns><see langword="true"/> if the signature matches any trusted key in the whitelist; otherwise, <see langword="false"/>.</returns>
    public bool VerifySignature(ReadOnlySpan<byte> trustedKeys) =>
        HasGSharpSignature && GpuSecurityPolicy.VerifySignature(Payload.Span, Signature.Span, trustedKeys);
}
