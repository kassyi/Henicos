namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Obsolete compatibility type for <see cref="ChudnovskyAlgorithm"/>.
/// </summary>
[Obsolete("Use ChudnovskyAlgorithm instead.")]
[ExcludeFromCodeCoverage]
public static class GpuChudnovskyAlgorithm
{
    /// <inheritdoc cref="ChudnovskyAlgorithm.ComputePi(int, TextWriter, int, bool)" />
    public static void ComputePi(int digits, TextWriter outputWriter, int thresholdTerms = 8192, bool asHex = false)
        => ChudnovskyAlgorithm.ComputePi(digits, outputWriter, thresholdTerms, asHex);
}
