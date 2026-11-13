using Kassyi.GSharp.Cuda;

namespace Kassyi.GSharp.Compiler.Targets;

/// <summary>The compute capability and kind of one <c>-gencode</c> code (<c>sm_86</c>, <c>compute_90a</c>).</summary>
/// <param name="Major">The major version (8 for <c>sm_86</c>, 10 for <c>sm_100f</c>).</param>
/// <param name="Minor">The minor version: the last digit.</param>
/// <param name="IsPtx">Whether the code is PTX (<c>compute_</c>) rather than SASS (<c>sm_</c>).</param>
/// <param name="Suffix"><c>a</c> (architecture-specific), <c>f</c> (family-specific) or <c>\0</c>.</param>
internal readonly record struct GencodeTarget(int Major, int Minor, bool IsPtx, char Suffix)
{
    // More digits than any compute capability has cannot name one
    private const int MaxDigits = 4;

    /// <summary>
    /// Reads a code that <see cref="CudaArchitectureList.IsArchitecture"/> accepted; <see langword="null"/> when its
    /// digits are too many to be a compute capability.
    /// </summary>
    public static GencodeTarget? Read(string code)
    {
        var isPtx = code.StartsWith(CudaArchitectureList.VirtualPrefix, StringComparison.Ordinal);
        var prefixLength = isPtx ? CudaArchitectureList.VirtualPrefix.Length : CudaArchitectureList.RealPrefix.Length;
        var digits = code.AsSpan(prefixLength);
        var suffix = digits[digits.Length - 1] is 'a' or 'f' ? digits[digits.Length - 1] : '\0';
        if (suffix != '\0')
        {
            digits = digits.Slice(0, digits.Length - 1);
        }

        if (digits.Length > MaxDigits)
        {
            return null;
        }

        var version = 0;
        foreach (var digit in digits)
        {
            version = version * 10 + (digit - '0');
        }

        return new GencodeTarget(version / 10, version % 10, isPtx, suffix);
    }

    /// <summary>
    /// Whether a GPU of <paramref name="row"/>'s compute capability runs this code: PTX runs on its own compute
    /// capability and every newer one, SASS on the newer minor versions of its major version (Tegra and desktop GPUs do
    /// not run each other's SASS), architecture-specific code on its own compute capability only, and family-specific
    /// code on the newer minor versions of its major version.
    /// </summary>
    public bool Runs(GpuArchitecture row) => Suffix switch
    {
        'a' => IsOwn(row),
        'f' => IsSameMajorAtLeast(row),
        _ when IsPtx => row.IsAtLeast(Major, Minor),
        _ => IsOwn(row) || (IsSameMajorAtLeast(row) && !row.IsTegra && !IsTegraCode),
    };

    // The Tegra compute capabilities of the table; their SASS runs on no other GPU
    private bool IsTegraCode => GpuArchitectureLimits.Table
        .AsStackEnumerable()
        .Any(this, static (row, code) => row.IsTegra && code.IsOwn(row));

    private bool IsOwn(GpuArchitecture row) => row.Major == Major && row.Minor == Minor;

    private bool IsSameMajorAtLeast(GpuArchitecture row) => row.Major == Major && row.Minor >= Minor;
}
