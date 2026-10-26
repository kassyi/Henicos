using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms.Diagnostics;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// GPU-accelerated Chudnovsky binary splitting algorithm for ultra-fast high-precision Pi computation.
/// Utilizes G# <see cref="GpuBigUInt"/> and NTT multiplication kernels with Zero-PCIe intermediate merges.
/// </summary>
public static partial class ChudnovskyAlgorithm
{
    /// <summary>
    /// Average number of decimal digits contributed per hypergeometric series term in the Chudnovsky algorithm.
    /// Derived from log10(640320^3 / 24) ≈ 14.181647462725477.
    /// </summary>
    private const double DigitsPerTerm = 14.181647462725477;

    /// <summary>
    /// Conversion factor from decimal digits to bits (log2(10)).
    /// </summary>
    private const double BitsPerDecimalDigit = 3.321928094887362;

    /// <summary>
    /// Average number of bits contributed per hypergeometric series term.
    /// </summary>
    private const double BitsPerTerm = DigitsPerTerm * BitsPerDecimalDigit;

    private const int ExtraDigits = 20;
    private const int ExtraBits = 1024;
    private const int MarginBits = 256;

    private class ProgressState(int totalTerms, int totalDigits)
    {
        public int TotalTerms
        {
            get;
        } = totalTerms;

        public int TotalDigits
        {
            get;
        } = totalDigits;

        public Stopwatch Stopwatch
        {
            get;
        } = Stopwatch.StartNew();

        public long LastReportTimeMs
        {
            get; set;
        }

        public void Report(int currentB)
        {
            var elapsed = Stopwatch.ElapsedMilliseconds;
            if (elapsed - LastReportTimeMs < 100 && currentB < TotalTerms)
            {
                return;
            }

            LastReportTimeMs = elapsed;

            var progress = (double)currentB / TotalTerms;
            var elapsedSec = elapsed / 1000.0;
            if (elapsedSec < 0.001)
            {
                elapsedSec = 0.001;
            }

            var termsPerSec = currentB / elapsedSec;
            var digitsPerSec = termsPerSec * DigitsPerTerm;

            var estimatedTotalSec = currentB > 0 ? elapsedSec / progress : 0;
            var etaSec = Math.Max(0, estimatedTotalSec - elapsedSec);

            var currentDigits = (int)Math.Min(TotalDigits, currentB * DigitsPerTerm);

            const int BarSize = 30;
            var filled = (int)(progress * BarSize);
            var bar = new string('█', filled) + new string('-', BarSize - filled);

            if (!Console.IsOutputRedirected)
            {
                Console.Write(
                    $"\r[GPU-Pi] [{bar}] {progress:P2} (Digits: {currentDigits:N0}/{TotalDigits:N0}) | Speed: {digitsPerSec:N0} digits/s | ETA: {TimeSpan.FromSeconds(etaSec):hh\\:mm\\:ss}   ");
            }
        }
    }

    /// <summary>
    /// Computes high-precision Pi using the Chudnovsky binary splitting algorithm on the GPU.
    /// </summary>
    /// <param name="digits">The number of decimal digits to compute.</param>
    /// <param name="outputWriter">The text writer to output the computed Pi.</param>
    /// <param name="thresholdTerms">The threshold number of terms to switch to CPU fallback.</param>
    /// <param name="asHex">If true, outputs in hexadecimal format instead of base 10.</param>
    public static void ComputePi(int digits, TextWriter outputWriter, int thresholdTerms = 8192,
        bool asHex = false)
    {
        ArgumentNullException.ThrowIfNull(outputWriter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdTerms);

        int totalDigits;
        int totalTerms;

        if (asHex)
        {
            var exactBits = (int)Math.Floor(digits * BitsPerDecimalDigit / 32) * 32;
            totalDigits = exactBits + ExtraBits + MarginBits;
            totalTerms = (int)Math.Ceiling(totalDigits / BitsPerTerm) + 1;
        }
        else
        {
            totalDigits = digits + ExtraDigits;
            totalTerms = (int)Math.Ceiling(digits / DigitsPerTerm) + 1;
        }

        // Estimate max limb words for GPU workspace allocation
        var estimatedWords = (int)((totalDigits * BitsPerDecimalDigit) / 32) + 256;
        var maxContextWords = Math.Max(4096, 1 << (int)Math.Ceiling(Math.Log2(estimatedWords * 4)));

        Logger.Info("GPU-Pi",
            $"Digits: {digits}, Terms: {totalTerms}, CPU/GPU Leaf Threshold: {thresholdTerms} terms");
        Logger.Info("GPU-Pi", $"Initializing CUDA Device & G# GPU Context (MaxWords: {maxContextWords:N0})...");

        using var cudaScope = new GpuKernelLoader.CudaContextScope();

        using var workspace = new GpuWorkspace(maxContextWords);
        var totalStopwatch = Stopwatch.StartNew();

        // 1. Compute Binary Splitting tree using Hybrid CPU/GPU execution
        var progressState = new ProgressState(totalTerms, digits);
        using var pqt = ComputeTree(0, totalTerms, workspace, thresholdTerms, progressState);
        var treeTimeMs = totalStopwatch.ElapsedMilliseconds;
        Logger.Info("GPU-Pi", $"Binary Splitting Tree computed in {treeTimeMs} ms");

        // 2. Final stage: Compute high-precision unscaled Pi fraction
        using var gpuPiExt = ComputeFinalPiFraction(workspace, pqt, digits, totalDigits, asHex);

        // 3. Fast D&C Radix Conversion & Streaming Output
        WritePiOutput(workspace, gpuPiExt, gpuPiExt.NumWords, digits, ExtraDigits, outputWriter, asHex);

        totalStopwatch.Stop();
        Logger.Info("GPU-Pi", $"Total Pipeline Time: {totalStopwatch.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Computes the unscaled high-precision Pi fraction from the binary splitting tree results:
    /// Pi = (426880 * Sqrt(10005 * 10^(2*totalDigits)) * Q) / T
    /// using GPU-accelerated NTT multiplication, Sqrt, and Newton-Raphson division.
    /// </summary>
    private static GpuBigUInt ComputeFinalPiFraction(GpuWorkspace workspace, HypergeometricTerms pqt, int digits,
        int totalDigits,
        bool asHex)
    {
        var finalStageStopwatch = Stopwatch.StartNew();

        using var gpuSqrtBase = ComputeSqrtBase(workspace, digits, totalDigits, asHex);
        using var gpuNumerator = ComputeNumerator(workspace, gpuSqrtBase, pqt.Q);
        var gpuPiExt = ExecuteFinalDivision(workspace, gpuNumerator, pqt.T.Value);

        var finalDivTimeMs = finalStageStopwatch.ElapsedMilliseconds;
        Logger.Info("GPU-Pi", $"Final division (Newton-Raphson GPU NTT) computed in {finalDivTimeMs} ms");

        return gpuPiExt;
    }

    private static GpuBigUInt ComputeSqrtBase(GpuWorkspace workspace, int digits, int totalDigits, bool asHex)
    {
        var swPow = Stopwatch.StartNew();
        GpuBigUInt gpuSqrtBase;

        if (asHex)
        {
            var exactBits = (int)Math.Floor(digits * BitsPerDecimalDigit / 32) * 32;
            var targetBits = exactBits + ExtraBits + MarginBits;

            Logger.Info("GPU-Pi", $"Computing Sqrt base (10005 * 2^{2 * targetBits})...");

            var bitShift = 2 * targetBits;
            var wordIndex = bitShift / 32;
            var bitIndex = bitShift % 32;
            var pow2Data = new uint[wordIndex + 1];
            pow2Data[wordIndex] = 1u << bitIndex;
            using var gpuPow2 = new GpuBigUInt(workspace, pow2Data);

            using var const10005 = GpuBigUInt.FromBigInteger(workspace, 10005);
            gpuSqrtBase = new GpuBigUInt(workspace, gpuPow2.NumWords + const10005.NumWords);
            GpuBigIntMath.Mul(gpuPow2, const10005, gpuSqrtBase);
        }
        else
        {
            // Decimal mode: Shift by 10^(2 * totalDigits)
            Logger.Info("GPU-Pi", $"Computing Sqrt base (10005 * 10^{2 * totalDigits})...");
            using var gpuPow10 = GpuBigIntMath.Pow10(workspace, 2 * totalDigits);
            using var const10005 = GpuBigUInt.FromBigInteger(workspace, 10005);
            gpuSqrtBase = new GpuBigUInt(workspace, gpuPow10.NumWords + const10005.NumWords);
            GpuBigIntMath.Mul(gpuPow10, const10005, gpuSqrtBase);
        }

        swPow.Stop();
        Logger.Info("GPU-Pi", $"Sqrt base computed in {swPow.ElapsedMilliseconds} ms");
        return gpuSqrtBase;
    }

    private static GpuBigUInt ComputeNumerator(GpuWorkspace workspace, GpuBigUInt gpuSqrtBase,
        GpuBigUInt pqtQ)
    {
        using var gpuSqrtResult = new GpuBigUInt(workspace, gpuSqrtBase.NumWords);
        var swSqrt = Stopwatch.StartNew();
        GpuBigIntDivRem.Sqrt(gpuSqrtBase, gpuSqrtResult);
        swSqrt.Stop();
        Logger.Info("GPU-Pi", $"Final division (Sqrt) computed in {swSqrt.ElapsedMilliseconds} ms");

        var swMul1 = Stopwatch.StartNew();
        using var gpuConstant = GpuBigUInt.FromBigInteger(workspace, 426880);
        var mulWords1 = gpuSqrtResult.NumWords + gpuConstant.NumWords;
        using var gpuSqrtWithConstant = new GpuBigUInt(workspace, mulWords1);
        GpuBigIntMath.Mul(gpuSqrtResult, gpuConstant, gpuSqrtWithConstant);
        swMul1.Stop();
        Logger.Info("GPU-Pi",
            $"Final division (Mul1: Sqrt * 426880) computed in {swMul1.ElapsedMilliseconds} ms");

        var swMul2 = Stopwatch.StartNew();
        var mulWords2 = gpuSqrtWithConstant.NumWords + pqtQ.NumWords;
        var gpuNumerator = new GpuBigUInt(workspace, mulWords2);
        GpuBigIntMath.Mul(gpuSqrtWithConstant, pqtQ, gpuNumerator);
        swMul2.Stop();
        Logger.Info("GPU-Pi", $"Final division (Mul2: * Q) computed in {swMul2.ElapsedMilliseconds} ms");

        return gpuNumerator;
    }

    private static GpuBigUInt ExecuteFinalDivision(GpuWorkspace workspace, GpuBigUInt numerator,
        GpuBigUInt denominator)
    {
        var targetWords = Math.Max(numerator.NumWords, denominator.NumWords);
        using GpuBigUInt? paddedNumerator = numerator.NumWords == targetWords ? null : numerator.PadTo(targetWords);
        using GpuBigUInt? paddedDenominator = denominator.NumWords == targetWords ? null : denominator.PadTo(targetWords);

        var gpuPiExt = new GpuBigUInt(workspace, targetWords);
        using var remainder = new GpuBigUInt(workspace, targetWords);

        // Execute Final Division entirely on GPU via Newton-Raphson NTT Division
        GpuBigIntDivRem.DivRem(paddedNumerator ?? numerator, paddedDenominator ?? denominator, gpuPiExt, remainder);

        return gpuPiExt;
    }

    /// <summary>
    /// Scales the extended Pi fraction down by 10^extraDigits and outputs the decimal or hexadecimal digits,
    /// writing to a file or displaying an abbreviated preview on the console.
    /// </summary>
    private static void WritePiOutput(
        GpuWorkspace workspace,
        GpuBigUInt gpuPiExt,
        int targetWords,
        int digits,
        int extraDigits,
        TextWriter outputWriter,
        bool asHex)
    {
        var radixStopwatch = Stopwatch.StartNew();

        if (asHex)
        {
            WriteHexPiOutput(gpuPiExt, outputWriter);
        }
        else
        {
            WriteDecimalPiOutput(workspace, gpuPiExt, targetWords, digits, extraDigits, outputWriter);
        }

        radixStopwatch.Stop();
        Logger.Info("GPU-Pi", $"Base Conversion & Output: {radixStopwatch.ElapsedMilliseconds} ms");
    }

    private static void WriteHexPiOutput(GpuBigUInt gpuPiExt, TextWriter hexWriter)
    {
        var hostData = gpuPiExt.ToHostArray();

        // Find the actual integer part (the first non-zero word from the top)
        var actualTopIndex = hostData.Length - 1;
        while (actualTopIndex >= 0 && hostData[actualTopIndex] == 0)
        {
            actualTopIndex--;
        }

        if (actualTopIndex < 0)
        {
            actualTopIndex = 0;
        }

        const int DropWords = (MarginBits + ExtraBits) / 32;
        const int TruncationSafetyWords = 8;

        const int CheckIndex = DropWords + TruncationSafetyWords;

        // The tail words CheckIndex + 1 and CheckIndex start at fractional hex digit (actualTopIndex - CheckIndex - 2) * 8,
        // which is non-negative only when word CheckIndex + 1 lies below the integer word.
        if (CheckIndex + 2 <= actualTopIndex)
        {
            var computedHexTail =
                hostData[CheckIndex + 1].ToString("X8") + hostData[CheckIndex].ToString("X8");
            var offset = (actualTopIndex - CheckIndex - 2) * 8;

            if (!PiVerifier.VerifyWithBbp(computedHexTail, offset))
            {
                throw new InvalidOperationException(
                    $"BBP hexadecimal verification failed at offset {offset} (computed tail: {computedHexTail}).");
            }
        }
        else if (!Console.IsOutputRedirected)
        {
            Logger.Error("Verification", "FAIL: Not enough valid words to verify with BBP safety margin.");
        }

        // Output integer part and fraction separated by a radix point
        hexWriter.Write(hostData[actualTopIndex].ToString("X"));
        hexWriter.Write('.');
        for (var i = actualTopIndex - 1; i >= DropWords; i--)
        {
            hexWriter.Write(hostData[i].ToString("X8"));
        }

        hexWriter.Flush();
    }

    private static void WriteDecimalPiOutput(
        GpuWorkspace workspace,
        GpuBigUInt gpuPiExt,
        int targetWords,
        int digits,
        int extraDigits,
        TextWriter writer)
    {
        // Remove extra digits from result by integer division: piExt = piExt / 10^extraDigits
        var extraDivisor = BigInteger.Pow(10, extraDigits);
        using var extraDivGpu = GpuBigUInt.FromBigInteger(workspace, extraDivisor, targetWords);
        using var finalPi = new GpuBigUInt(workspace, targetWords);
        using var finalRem = new GpuBigUInt(workspace, targetWords);
        using GpuBigUInt? paddedPiExt = gpuPiExt.NumWords == targetWords ? null : gpuPiExt.PadTo(targetWords);
        GpuBigIntDivRem.DivRem(paddedPiExt ?? gpuPiExt, extraDivGpu, finalPi, finalRem);

        GpuRadixConverter.ConvertAndWrite(workspace, finalPi, digits + 1, writer, isMostSignificant: true);
        writer.Flush();
    }

    private static HypergeometricTerms ComputeTree(int a, int b, GpuWorkspace workspace, int thresholdTerms,
        ProgressState state)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdTerms);
        var terms = b - a;
        if (terms <= thresholdTerms)
        {
            var cpuResult = ComputeSubtree(a, b);
            var gpuResult = UploadToGpu(workspace, cpuResult);
            state.Report(b);
            return gpuResult;
        }

        var m = (a + b) / 2;
        using var left = ComputeTree(a, m, workspace, thresholdTerms, state);
        using var right = ComputeTree(m, b, workspace, thresholdTerms, state);

        var result = HypergeometricTerms.Merge(workspace, left, right);

        state.Report(b);
        return result;
    }

    private static HypergeometricTerms UploadToGpu(GpuWorkspace workspace, ChudnovskyPqt cpu)
    {
        var gpuP = ToGpuBigInt(workspace, cpu.P);
        var gpuQ = GpuBigUInt.FromBigInteger(workspace, cpu.Q);
        var gpuT = ToGpuBigInt(workspace, cpu.T);

        return new HypergeometricTerms(gpuP, gpuQ, gpuT);
    }

    private static GpuBigInt ToGpuBigInt(GpuWorkspace workspace, BigInteger value)
    {
        var isNegative = value < 0;
        var absValue = BigInteger.Abs(value);
        var gpuValue = GpuBigUInt.FromBigInteger(workspace, absValue);
        return new GpuBigInt(gpuValue, isNegative);
    }
}

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

