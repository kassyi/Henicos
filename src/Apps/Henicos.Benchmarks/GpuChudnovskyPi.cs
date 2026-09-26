using System.Numerics;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.IO;

namespace Henicos.Benchmarks;

/// <summary>
/// GPU-accelerated Chudnovsky binary splitting algorithm for ultra-fast high-precision Pi computation.
/// Utilizes G# <see cref="GpuUIntBigInteger"/> and NTT multiplication kernels with Zero-PCIe intermediate merges.
/// </summary>
public static class GpuChudnovskyPi
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
        } = 0;

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

            Console.Write(
                $"\r[GPU-Pi] [{bar}] {progress:P2} (Digits: {currentDigits:N0}/{TotalDigits:N0}) | Speed: {digitsPerSec:N0} digits/s | ETA: {TimeSpan.FromSeconds(etaSec):hh\\:mm\\:ss}   ");
        }
    }

    public static void ComputePi(int digits, TextWriter outputWriter, int thresholdTerms = 8192,
        bool asHex = false)
    {
        ArgumentNullException.ThrowIfNull(outputWriter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdTerms);

        int totalDigits;
        int totalTerms;
        var extraDigits = 20;

        if (asHex)
        {
            var exactBits = (int)Math.Floor(digits * BitsPerDecimalDigit / 32) * 32;
            var extraBits = 1024; // Safe margin for series truncation error
            var marginBits = 256; // Division noise margin
            totalDigits = exactBits + extraBits + marginBits;
            totalTerms = (int)Math.Ceiling(totalDigits / DigitsPerTerm);
        }
        else
        {
            totalDigits = digits + extraDigits;
            totalTerms = (int)Math.Ceiling(digits / DigitsPerTerm) + 1;
        }

        // Estimate max limb words for GPU workspace allocation
        var estimatedWords = (int)((totalDigits * BitsPerDecimalDigit) / 32) + 256;
        var maxContextWords = Math.Max(4096, 1 << (int)Math.Ceiling(Math.Log2(estimatedWords * 4)));

        Logger.Info("GPU-Pi",
            $"Digits: {digits}, Terms: {totalTerms}, CPU/GPU Leaf Threshold: {thresholdTerms} terms");
        Logger.Info("GPU-Pi", $"Initializing CUDA Device & G# GPU Context (MaxWords: {maxContextWords:N0})...");

        using var cudaScope = new GpuKernelLoader.CudaContextScope();

        using var context = new GpuContext(maxContextWords);
        var totalStopwatch = Stopwatch.StartNew();

        // 1. Compute Binary Splitting tree using Hybrid CPU/GPU execution
        var progressState = new ProgressState(totalTerms, digits);
        using var pqt = ComputeTree(0, totalTerms, context, thresholdTerms, progressState);
        var treeTimeMs = totalStopwatch.ElapsedMilliseconds;
        Logger.Info("GPU-Pi", $"Binary Splitting Tree computed in {treeTimeMs} ms");

        // 2. Final stage: Compute high-precision unscaled Pi fraction
        using var gpuPiExt = ComputeFinalPiFraction(context, pqt, digits, totalDigits, asHex);

        // 3. Fast D&C Radix Conversion & Streaming Output
        WritePiOutput(context, gpuPiExt, gpuPiExt.NumWords, digits, extraDigits, outputWriter, asHex);

        totalStopwatch.Stop();
        Logger.Info("GPU-Pi", $"Total Pipeline Time: {totalStopwatch.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Computes the unscaled high-precision Pi fraction from the binary splitting tree results:
    /// Pi = (426880 * Sqrt(10005 * 10^(2*totalDigits)) * Q) / T
    /// using GPU-accelerated NTT multiplication, Sqrt, and Newton-Raphson division.
    /// </summary>
    private static GpuUIntBigInteger ComputeFinalPiFraction(GpuContext context, GpuHypergeometricPqt pqt, int digits,
        int totalDigits,
        bool asHex)
    {
        var finalStageStopwatch = Stopwatch.StartNew();

        var gpuSqrtBase = ComputeSqrtBase(context, digits, totalDigits, asHex);
        using var gpuNumerator = ComputeNumerator(context, gpuSqrtBase, pqt.Q);
        var gpuPiExt = ExecuteFinalDivision(context, gpuNumerator, pqt.T.Value);

        var finalDivTimeMs = finalStageStopwatch.ElapsedMilliseconds;
        Logger.Info("GPU-Pi", $"Final division (Newton-Raphson GPU NTT) computed in {finalDivTimeMs} ms");

        return gpuPiExt;
    }

    private static GpuUIntBigInteger ComputeSqrtBase(GpuContext context, int digits, int totalDigits, bool asHex)
    {
        var swPow = Stopwatch.StartNew();
        GpuUIntBigInteger gpuSqrtBase;

        if (asHex)
        {
            var exactBits = (int)Math.Floor(digits * 3.32192809489 / 32) * 32;
            var extraBits = 1024; // Safe margin for series truncation error
            var marginBits = 256; // Division noise margin
            var targetBits = exactBits + extraBits + marginBits;

            Logger.Info("GPU-Pi", $"Computing Sqrt base (10005 * 2^{2 * targetBits})...");

            var bitShift = 2 * targetBits;
            var wordIndex = bitShift / 32;
            var bitIndex = bitShift % 32;
            var pow2Data = new uint[wordIndex + 1];
            pow2Data[wordIndex] = 1u << bitIndex;
            using var gpuPow2 = new GpuUIntBigInteger(context, pow2Data);

            using var const10005 = GpuUIntBigInteger.FromBigInteger(context, 10005, 0);
            gpuSqrtBase = new GpuUIntBigInteger(context, gpuPow2.NumWords + const10005.NumWords);
            GpuBigIntMath.Mul(gpuPow2, const10005, gpuSqrtBase);
        }
        else
        {
            // Decimal mode: Shift by 10^(2 * totalDigits)
            Logger.Info("GPU-Pi", $"Computing Sqrt base (10005 * 10^{2 * totalDigits})...");
            using var gpuPow10 = GpuBigIntMath.Pow10(context, 2 * totalDigits);
            using var const10005 = GpuUIntBigInteger.FromBigInteger(context, 10005, 0);
            gpuSqrtBase = new GpuUIntBigInteger(context, gpuPow10.NumWords + const10005.NumWords);
            GpuBigIntMath.Mul(gpuPow10, const10005, gpuSqrtBase);
        }

        swPow.Stop();
        Logger.Info("GPU-Pi", $"Sqrt base computed in {swPow.ElapsedMilliseconds} ms");
        return gpuSqrtBase;
    }

    private static GpuUIntBigInteger ComputeNumerator(GpuContext context, GpuUIntBigInteger gpuSqrtBase,
        GpuUIntBigInteger pqtQ)
    {
        using var gpuSqrtResult = new GpuUIntBigInteger(context, gpuSqrtBase.NumWords);
        var swSqrt = Stopwatch.StartNew();
        GpuBigIntDivRem.Sqrt(gpuSqrtBase, gpuSqrtResult);
        swSqrt.Stop();
        gpuSqrtBase.Dispose();
        Logger.Info("GPU-Pi", $"Final division (Sqrt) computed in {swSqrt.ElapsedMilliseconds} ms");

        var swMul1 = Stopwatch.StartNew();
        using var gpuConstant = GpuUIntBigInteger.FromBigInteger(context, 426880, 0);
        var mulWords1 = gpuSqrtResult.NumWords + gpuConstant.NumWords;
        using var gpuSqrtWithConstant = new GpuUIntBigInteger(context, mulWords1);
        GpuBigIntMath.Mul(gpuSqrtResult, gpuConstant, gpuSqrtWithConstant);
        swMul1.Stop();
        Logger.Info("GPU-Pi",
            $"Final division (Mul1: Sqrt * 426880) computed in {swMul1.ElapsedMilliseconds} ms");

        var swMul2 = Stopwatch.StartNew();
        var mulWords2 = gpuSqrtWithConstant.NumWords + pqtQ.NumWords;
        var gpuNumerator = new GpuUIntBigInteger(context, mulWords2);
        GpuBigIntMath.Mul(gpuSqrtWithConstant, pqtQ, gpuNumerator);
        swMul2.Stop();
        Logger.Info("GPU-Pi", $"Final division (Mul2: * Q) computed in {swMul2.ElapsedMilliseconds} ms");

        return gpuNumerator;
    }

    private static GpuUIntBigInteger ExecuteFinalDivision(GpuContext context, GpuUIntBigInteger numerator,
        GpuUIntBigInteger denominator)
    {
        var targetWords = Math.Max(numerator.NumWords, denominator.NumWords);
        using var paddedNumerator = numerator.PadTo(targetWords);
        using var paddedDenominator = denominator.PadTo(targetWords);

        var gpuPiExt = new GpuUIntBigInteger(context, targetWords);
        using var remainder = new GpuUIntBigInteger(context, targetWords);

        // Execute Final Division entirely on GPU via Newton-Raphson NTT Division
        GpuBigIntDivRem.DivRem(paddedNumerator, paddedDenominator, gpuPiExt, remainder);

        return gpuPiExt;
    }

    /// <summary>
    /// Scales the extended Pi fraction down by 10^extraDigits and outputs the decimal or hexadecimal digits,
    /// writing to a file or displaying an abbreviated preview on the console.
    /// </summary>
    private static void WritePiOutput(
        GpuContext context,
        GpuUIntBigInteger gpuPiExt,
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
            WriteDecimalPiOutput(context, gpuPiExt, targetWords, digits, extraDigits, outputWriter);
        }

        radixStopwatch.Stop();
        Logger.Info("GPU-Pi", $"Base Conversion & Output: {radixStopwatch.ElapsedMilliseconds} ms");
    }

    private static void WriteHexPiOutput(GpuUIntBigInteger gpuPiExt, TextWriter hexWriter)
    {
        // Exact bits that are guaranteed to be correct (word-aligned)
        const int ExtraBits = 1024; // Safe margin for series truncation error
        const int MarginBits = 256; // Division noise margin

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

        if (CheckIndex + 1 <= actualTopIndex)
        {
            var computedHexTail =
                hostData[CheckIndex + 1].ToString("X8") + hostData[CheckIndex].ToString("X8");
            var offset = (actualTopIndex - CheckIndex - 2) * 8;

            PiVerification.VerifyWithBbp(computedHexTail, offset);
        }
        else
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
        GpuContext context,
        GpuUIntBigInteger gpuPiExt,
        int targetWords,
        int digits,
        int extraDigits,
        TextWriter writer)
    {
        // Remove extra digits from result by integer division: piExt = piExt / 10^extraDigits
        var extraDivisor = BigInteger.Pow(10, extraDigits);
        using var extraDivGpu = GpuUIntBigInteger.FromBigInteger(context, extraDivisor, targetWords);
        using var finalPi = new GpuUIntBigInteger(context, targetWords);
        using var finalRem = new GpuUIntBigInteger(context, targetWords);
        using var paddedPiExt = gpuPiExt.PadTo(targetWords);
        GpuBigIntDivRem.DivRem(paddedPiExt, extraDivGpu, finalPi, finalRem);

        GpuRadixConverter.ConvertAndWrite(context, finalPi, digits + 1, writer, isMostSignificant: true);
        writer.Flush();
    }

    private static GpuHypergeometricPqt ComputeTree(int a, int b, GpuContext context, int thresholdTerms,
        ProgressState state)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdTerms);
        var terms = b - a;
        if (terms <= thresholdTerms)
        {
            var cpuResult = ChudnovskyPi.ComputeSubtree(a, b);
            var gpuResult = UploadToGpu(context, cpuResult);
            state.Report(b);
            return gpuResult;
        }

        var m = (a + b) / 2;
        var swLeft = Stopwatch.StartNew();
        using var left = ComputeTree(a, m, context, thresholdTerms, state);
        swLeft.Stop();
        var swRight = Stopwatch.StartNew();
        using var right = ComputeTree(m, b, context, thresholdTerms, state);
        swRight.Stop();

        var swMerge = Stopwatch.StartNew();
        var result = GpuHypergeometricPqt.Merge(context, left, right);
        swMerge.Stop();

        state.Report(b);
        return result;
    }


    private static GpuHypergeometricPqt UploadToGpu(GpuContext context, ChudnovskyPi.ChudnovskyPqt cpu)
    {
        var sw = Stopwatch.StartNew();
        var pNeg = cpu.P < 0;
        var absP = BigInteger.Abs(cpu.P);
        var gpuP = GpuUIntBigInteger.FromBigInteger(context, absP, 0);

        var gpuQ = GpuUIntBigInteger.FromBigInteger(context, cpu.Q, 0);

        var tNeg = cpu.T < 0;
        var absT = BigInteger.Abs(cpu.T);
        var gpuT = GpuUIntBigInteger.FromBigInteger(context, absT, 0);

        var result = new GpuHypergeometricPqt(
            new GpuBigInteger(gpuP, pNeg),
            gpuQ,
            new GpuBigInteger(gpuT, tNeg)
        );
        sw.Stop();
        Logger.Debug("GPU-Pi", $"UploadToGpu took {sw.ElapsedMilliseconds} ms");
        return result;
    }
}
