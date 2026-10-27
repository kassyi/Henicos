namespace Kassyi.GSharp.Runtime.Fatbin;

/// <summary>
/// Represents metadata for an embedded module (cuBIN or PTX) within an NVIDIA FATBIN binary.
/// </summary>
/// <param name="type">The type of embedded module (1 = PTX, 2 = ELF/cuBIN).</param>
/// <param name="major">The architecture major version.</param>
/// <param name="minor">The architecture minor version.</param>
/// <param name="computeVersion">The compute capability version number.</param>
/// <param name="objectName">The optional object name identifier.</param>
/// <param name="dataSize">The size in bytes of the embedded module data payload.</param>
public readonly struct FatbinItemInfo(
    ushort type,
    ushort major,
    ushort minor,
    uint computeVersion,
    string? objectName,
    ulong dataSize)
{
    /// <summary>
    /// Type identifier for PTX intermediate assembly modules (1).
    /// </summary>
    public const ushort TypePtx = 1;

    /// <summary>
    /// Type identifier for native ELF / cuBIN machine code modules (2).
    /// </summary>
    public const ushort TypeCubin = 2;

    /// <summary>
    /// Alias for <see cref="TypeCubin"/> representing ELF binary modules (2).
    /// </summary>
    public const ushort TypeElf = TypeCubin;

    /// <summary>
    /// Gets the type of embedded file (1 = PTX, 2 = ELF/cuBIN).
    /// </summary>
    public ushort Type => type;

    /// <summary>
    /// Gets the architecture major version (e.g. 8 for sm_80).
    /// </summary>
    public ushort Major => major;

    /// <summary>
    /// Gets the architecture minor version (e.g. 0 for sm_80, 6 for sm_86).
    /// </summary>
    public ushort Minor => minor;

    /// <summary>
    /// Gets the raw compute capability version (e.g. 80 for sm_80, 86 for sm_86).
    /// </summary>
    public uint ComputeVersion => computeVersion;

    /// <summary>
    /// Gets the optional object name or identifier assigned to this embedded module.
    /// </summary>
    public string? ObjectName => objectName;

    /// <summary>
    /// Gets the size in bytes of the embedded module data payload.
    /// </summary>
    public ulong DataSize => dataSize;

    /// <summary>
    /// Verifies if this embedded module is compatible with the specified GPU compute capability.
    /// </summary>
    /// <param name="deviceMajor">The major compute capability version of the physical GPU (e.g. 8 for sm_80).</param>
    /// <param name="deviceMinor">The minor compute capability version of the physical GPU (e.g. 0 for sm_80, 6 for sm_86).</param>
    /// <returns><see langword="true"/> if this module can execute on the target architecture; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// A PTX module runs on any device whose compute capability is equal to or newer than its target (the driver
    /// JIT-compiles it). A cuBIN module runs on devices of the same major version whose minor version is equal or newer
    /// (an <c>sm_86</c> cuBIN runs on 8.6 and 8.9, but not on 8.0 or 9.0). Architecture-specific (<c>sm_90a</c>) and
    /// family-specific (<c>sm_100f</c>) cuBINs, and Tegra GPUs (8.7, Jetson Orin), which are not binary compatible with
    /// the desktop GPUs of the same major version, follow stricter or wider rules that this check does not model; it
    /// errs on the side of accepting what the driver may accept and leaves the final decision to the driver.
    /// </remarks>
    public bool IsCompatibleWith(int deviceMajor, int deviceMinor)
    {
        if (deviceMajor < 0 || deviceMinor < 0)
        {
            return false;
        }

        return Type switch
        {
            // PTX: Forward compatible JIT compilation.
            // Can run if device architecture is equal to or newer than the PTX target architecture.
            TypePtx => deviceMajor > Major || (deviceMajor == Major && deviceMinor >= Minor),

            // cuBIN (native ELF binary): binary compatible within one major version, from its own minor version up.
            TypeCubin => Major == deviceMajor && deviceMinor >= Minor,

            // Fallback for custom or future types: check compute capability version bounds
            _ => deviceMajor * 10 + deviceMinor == (int)ComputeVersion
        };
    }
}
