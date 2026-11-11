namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class FermatTrialFactoringKernels
{
    /// <summary>
    /// Searches the odd candidates <c>startOdd + 2t</c> for factors of <c>N(exp) = 2^(2 exp + 1) - 2^exp + 11</c> with
    /// <see cref="SearchFactors32Kernel"/>, after checking the arguments.
    /// </summary>
    /// <param name="startOdd">The first candidate; thread <c>t</c> tests <c>startOdd + 2t</c>.</param>
    /// <param name="exp">The exponent of <c>N</c>.</param>
    /// <param name="results">The array that receives the factors found, up to <paramref name="maxResults"/>.</param>
    /// <param name="resultCount">A one-element array whose element is increased by the number of factors found.</param>
    /// <param name="maxResults">
    /// The number of factors stored at most, from 0 to the length of <paramref name="results"/>.
    /// </param>
    /// <param name="gridDimX">The number of blocks.</param>
    /// <param name="blockDimX">The number of threads per block.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="results"/> or <paramref name="resultCount"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxResults"/> is negative or greater than the length of <paramref name="results"/>.
    /// </exception>
    public static void SearchFactors32(
        uint startOdd,
        uint exp,
        uint[] results,
        uint[] resultCount,
        int maxResults,
        uint gridDimX,
        uint blockDimX)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(resultCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxResults);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, results.Length);

        LaunchSearchFactors32Kernel(startOdd, exp, results, resultCount, maxResults, gridDimX: gridDimX,
            blockDimX: blockDimX);
    }

    /// <summary>
    /// Searches the odd candidates <c>startOdd + 2t</c> for factors of <c>N(exp) = 2^(2 exp + 1) - 2^exp + 11</c> with
    /// <see cref="SearchFactors32Kernel"/> on device buffers, after checking the arguments.
    /// </summary>
    /// <param name="startOdd">The first candidate; thread <c>t</c> tests <c>startOdd + 2t</c>.</param>
    /// <param name="exp">The exponent of <c>N</c>.</param>
    /// <param name="results">The device buffer that receives the factors found (for example
    /// <see cref="Kassyi.GSharp.Runtime.GpuBuffer{T}.ToProxy"/>); it must hold <paramref name="maxResults"/> elements.</param>
    /// <param name="resultCount">The device word that is increased by the number of factors found.</param>
    /// <param name="maxResults">The number of factors stored at most.</param>
    /// <param name="gridDimX">The number of blocks.</param>
    /// <param name="blockDimX">The number of threads per block.</param>
    /// <param name="stream">The CUDA stream of the launch.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxResults"/> is negative or greater than the
    /// length of <paramref name="results"/>.</exception>
    public static void SearchFactors32(
        uint startOdd,
        uint exp,
        GSharpArrayProxy results,
        GSharpArrayProxy resultCount,
        int maxResults,
        uint gridDimX,
        uint blockDimX,
        IntPtr stream = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxResults);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, results.Length);

        LaunchSearchFactors32KernelPtr(startOdd, exp, results, resultCount, maxResults, gridDimX: gridDimX,
            blockDimX: blockDimX, hStream: stream);
    }
}
