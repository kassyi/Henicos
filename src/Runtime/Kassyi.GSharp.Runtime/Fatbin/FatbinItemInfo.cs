namespace Kassyi.GSharp.Runtime.Fatbin;

/// <summary>
/// Represents metadata for an embedded module (cuBIN or PTX) within an NVIDIA FATBIN binary.
/// </summary>
public readonly struct FatbinItemInfo
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
    public ushort Type { get; }

    /// <summary>
    /// Gets the architecture major version (e.g. 8 for sm_80).
    /// </summary>
    public ushort Major { get; }

    /// <summary>
    /// Gets the architecture minor version (e.g. 0 for sm_80, 6 for sm_86).
    /// </summary>
    public ushort Minor { get; }

    /// <summary>
    /// Gets the raw compute capability version (e.g. 80 for sm_80, 86 for sm_86).
    /// </summary>
    public uint ComputeVersion { get; }

    /// <summary>
    /// Gets the optional object name or identifier assigned to this embedded module.
    /// </summary>
    public string? ObjectName { get; }

    /// <summary>
    /// Gets the size in bytes of the embedded module data payload.
    /// </summary>
    public ulong DataSize { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FatbinItemInfo"/> struct.
    /// </summary>
    /// <param name="type">The type of embedded module (1 = PTX, 2 = ELF/cuBIN).</param>
    /// <param name="major">The architecture major version.</param>
    /// <param name="minor">The architecture minor version.</param>
    /// <param name="computeVersion">The compute capability version number.</param>
    /// <param name="objectName">The optional object name identifier.</param>
    /// <param name="dataSize">The size in bytes of the embedded module data payload.</param>
    public FatbinItemInfo(
        ushort type,
        ushort major,
        ushort minor,
        uint computeVersion,
        string? objectName,
        ulong dataSize)
    {
        Type = type;
        Major = major;
        Minor = minor;
        ComputeVersion = computeVersion;
        ObjectName = objectName;
        DataSize = dataSize;
    }
}
