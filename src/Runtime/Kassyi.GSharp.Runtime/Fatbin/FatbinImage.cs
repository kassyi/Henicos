namespace Kassyi.GSharp.Runtime.Fatbin;

/// <summary>
/// Represents a parsed NVIDIA FATBIN binary image and its embedded targets.
/// </summary>
public sealed class FatbinImage
{
    /// <summary>
    /// Gets the FATBIN magic number (expected to be <c>0xBA55ED50</c>).
    /// </summary>
    public uint Magic
    {
        get;
    }

    /// <summary>
    /// Gets the FATBIN format version.
    /// </summary>
    public ushort Version
    {
        get;
    }

    /// <summary>
    /// Gets the total size of the FATBIN binary payload in bytes (including headers, excluding appended signatures).
    /// </summary>
    public ulong PayloadSize
    {
        get;
    }

    /// <summary>
    /// Gets a value indicating whether a G# 32-byte SHA-256 signature is detected at the tail.
    /// Determined strictly by whether the source binary length equals <see cref="PayloadSize"/> + 32.
    /// </summary>
    public bool HasGSharpSignature
    {
        get;
    }

    /// <summary>
    /// Gets the raw FATBIN binary payload (excluding signature).
    /// </summary>
    public ReadOnlyMemory<byte> Payload
    {
        get;
    }

    /// <summary>
    /// Gets the appended 32-byte G# signature, or an empty memory slice if not signed.
    /// </summary>
    public ReadOnlyMemory<byte> Signature
    {
        get;
    }

    /// <summary>
    /// Gets the metadata of all embedded modules (PTX or cuBIN) contained in this fatbin.
    /// </summary>
    public IReadOnlyList<FatbinItemInfo> Items
    {
        get;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FatbinImage"/> class.
    /// </summary>
    /// <param name="magic">The FATBIN magic number.</param>
    /// <param name="version">The FATBIN format version.</param>
    /// <param name="payloadSize">The total payload size in bytes.</param>
    /// <param name="hasGSharpSignature">Whether a G# 32-byte SHA-256 signature is present.</param>
    /// <param name="payload">The binary payload data slice.</param>
    /// <param name="signature">The appended signature slice, if present.</param>
    /// <param name="items">The collection of parsed embedded modules.</param>
    public FatbinImage(
        uint magic,
        ushort version,
        ulong payloadSize,
        bool hasGSharpSignature,
        ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte> signature,
        IReadOnlyList<FatbinItemInfo> items)
    {
        Magic = magic;
        Version = version;
        PayloadSize = payloadSize;
        HasGSharpSignature = hasGSharpSignature;
        Payload = payload;
        Signature = signature;
        Items = items;
    }

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
            switch (item.Type)
            {
                case FatbinItemInfo.TypePtx:
                    {
                        // PTX: Forward compatible JIT compilation.
                        // Can run if device architecture is equal to or newer than the PTX target architecture.
                        if (deviceMajor > item.Major || (deviceMajor == item.Major && deviceMinor >= item.Minor))
                        {
                            return true;
                        }

                        break;
                    }
                case FatbinItemInfo.TypeCubin:
                    {
                        // cuBIN (native ELF binary): Requires exact architecture match.
                        if (item.Major == deviceMajor && item.Minor == deviceMinor)
                        {
                            return true;
                        }

                        break;
                    }
                default:
                    {
                        // Fallback for custom or future types: check compute capability version bounds
                        var targetVersion = deviceMajor * 10 + deviceMinor;
                        if (targetVersion == (int)item.ComputeVersion)
                        {
                            return true;
                        }

                        break;
                    }
            }

        }

        return false;
    }
}
