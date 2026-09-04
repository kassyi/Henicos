using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.IO;

namespace Henicos.PiBenchmark;

/// <summary>
/// GPU-accelerated Chudnovsky binary splitting algorithm for ultra-fast high-precision Pi computation.
/// Utilizes G# <see cref="GpuBigInteger"/> and NTT multiplication kernels with Zero-PCIe intermediate merges.
/// </summary>
public static class GpuChudnovskyPi
{
    private readonly struct GpuPqt : IDisposable
    {
        public SignedGpuBigInteger P { get; }
        public GpuBigInteger Q { get; }
        public SignedGpuBigInteger T { get; }

        public GpuPqt(SignedGpuBigInteger p, GpuBigInteger q, SignedGpuBigInteger t)
        {
            P = p;
            Q = q;
            T = t;
        }

        public void Dispose()
        {
            P.Dispose();
            Q.Dispose();
            T.Dispose();
        }
    }

    private readonly struct CpuPqt
    {
        public BigInteger P { get; init; }
        public BigInteger Q { get; init; }
        public BigInteger T { get; init; }
    }

    private class ProgressState
    {
        public int TotalTerms { get; }
        public int TotalDigits { get; }
        public Stopwatch Stopwatch { get; }
        public long LastReportTimeMs { get; set; }

        public ProgressState(int totalTerms, int totalDigits)
        {
            TotalTerms = totalTerms;
            TotalDigits = totalDigits;
            Stopwatch = Stopwatch.StartNew();
            LastReportTimeMs = 0;
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
            var digitsPerSec = termsPerSec * 14.181647462725477;

            var estimatedTotalSec = currentB > 0 ? elapsedSec / progress : 0;
            var etaSec = Math.Max(0, estimatedTotalSec - elapsedSec);

            var currentDigits = (int)Math.Min(TotalDigits, currentB * 14.181647462725477);

            var barSize = 30;
            var filled = (int)(progress * barSize);
            var bar = new string('█', filled) + new string('-', barSize - filled);

            Console.Write(
                $"\r[GPU-Pi] [{bar}] {progress:P2} (Digits: {currentDigits:N0}/{TotalDigits:N0}) | Speed: {digitsPerSec:N0} digits/s | ETA: {TimeSpan.FromSeconds(etaSec):hh\\:mm\\:ss}   ");
        }
    }

    /// <summary>
    /// Computes high-precision digits of Pi using the hybrid CPU/GPU Chudnovsky algorithm,
    /// executing the final division on GPU and streaming output directly to a file via D&amp;C Radix Conversion.
    /// </summary>
    /// <param name="digits">Target number of decimal digits.</param>
    /// <param name="outputPath">Optional file path to write digits to.</param>
    /// <param name="thresholdTerms">Maximum number of terms evaluated on CPU before transitioning to GPU NTT merges.</param>
    public static void ComputePiAndWriteToFile(int digits, string? outputPath, int thresholdTerms = 8192,
        bool asHex = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);

        int totalDigits;
        int totalTerms;
        int extraDigits = 20;

        if (asHex)
        {
            int exactBits = (int)Math.Floor(digits * 3.32192809489 / 32) * 32;
            int extraBits = 1024; // Safe margin for series truncation error
            int marginBits = 256; // Division noise margin
            totalDigits = exactBits + extraBits + marginBits;
            totalTerms = (int)Math.Ceiling(totalDigits / 14.181647462725477);
        }
        else
        {
            totalDigits = digits + extraDigits;
            totalTerms = digits / 14 + 1;
        }

        // Estimate max limb words for GPU workspace allocation
        var estimatedWords = (int)((totalDigits * 3.32192809489) / 32) + 256;
        var maxContextWords = Math.Max(4096, 1 << (int)Math.Ceiling(Math.Log2(estimatedWords * 4)));

        Console.WriteLine(
            $"[GPU-Pi] Digits: {digits}, Terms: {totalTerms}, CPU/GPU Leaf Threshold: {thresholdTerms} terms");
        Console.WriteLine($"[GPU-Pi] Initializing CUDA Device & G# GPU Context (MaxWords: {maxContextWords:N0})...");

        CudaException.ThrowIfError(CudaDriverApi.cuInit(0));
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out var cudaContext, 0, device));

        try
        {
            using var context = new GpuContext(maxContextWords, GpuExecutionEngine.GSharpJit);
            var totalStopwatch = Stopwatch.StartNew();

            // 1. Compute Binary Splitting tree using Hybrid CPU/GPU execution
            var progressState = new ProgressState(totalTerms, digits);
            using var pqt = ComputeTree(0, totalTerms, context, thresholdTerms, progressState);
            var treeTimeMs = totalStopwatch.ElapsedMilliseconds;
            Console.WriteLine($"\n[GPU-Pi] Binary Splitting Tree computed in {treeTimeMs} ms");

            // 2. Final stage: Compute high-precision unscaled Pi fraction
            using var gpuPiExt = ComputeFinalPiFraction(context, pqt, digits, totalDigits, asHex);

            // 3. Fast D&C Radix Conversion & Streaming Output
            WritePiOutput(context, gpuPiExt, gpuPiExt.NumWords, digits, extraDigits, outputPath, asHex);

            totalStopwatch.Stop();
            Console.WriteLine($"[GPU-Pi] Total Pipeline Time: {totalStopwatch.ElapsedMilliseconds} ms");
        }
        finally
        {
            CudaDriverApi.cuCtxDestroy_v2(cudaContext);
        }
    }

    /// <summary>
    /// Computes the unscaled high-precision Pi fraction from the binary splitting tree results:
    /// Pi = (426880 * Sqrt(10005 * 10^(2*totalDigits)) * Q) / T
    /// using GPU-accelerated NTT multiplication, Sqrt, and Newton-Raphson division.
    /// </summary>
    private static GpuBigInteger ComputeFinalPiFraction(GpuContext context, GpuPqt pqt, int digits, int totalDigits, bool asHex)
    {
        var finalStageStopwatch = Stopwatch.StartNew();
        var swPow = Stopwatch.StartNew();

        GpuBigInteger gpuSqrtBase;

        if (asHex)
        {
            int exactBits = (int)Math.Floor(digits * 3.32192809489 / 32) * 32;
            int extraBits = 1024; // Safe margin for series truncation error
            int marginBits = 256; // Division noise margin
            int targetBits = exactBits + extraBits + marginBits;

            Console.WriteLine($"[GPU-Pi] Computing Sqrt base (10005 * 2^{2 * targetBits})...");
            
            int bitShift = 2 * targetBits;
            int wordIndex = bitShift / 32;
            int bitIndex = bitShift % 32;
            uint[] pow2Data = new uint[wordIndex + 1];
            pow2Data[wordIndex] = 1u << bitIndex;
            using var gpuPow2 = new GpuBigInteger(context, pow2Data);
            
            using var const10005 = ToGpuBigInt(context, 10005, 0);
            gpuSqrtBase = new GpuBigInteger(context, gpuPow2.NumWords + const10005.NumWords);
            GpuBigIntMath.Mul(gpuPow2, const10005, gpuSqrtBase);
        }
        else
        {
            // Decimal mode: Shift by 10^(2 * totalDigits)
            Console.WriteLine($"[GPU-Pi] Computing Sqrt base (10005 * 10^{2 * totalDigits})...");
            using var gpuPow10 = GpuPow10(context, 2 * totalDigits);
            using var const10005 = ToGpuBigInt(context, 10005, 0);
            gpuSqrtBase = new GpuBigInteger(context, gpuPow10.NumWords + const10005.NumWords);
            GpuBigIntMath.Mul(gpuPow10, const10005, gpuSqrtBase);
        }

        swPow.Stop();
        Console.WriteLine($"[GPU-Pi] Sqrt base computed in {swPow.ElapsedMilliseconds} ms");

        using var gpuSqrtResult = new GpuBigInteger(context, gpuSqrtBase.NumWords);
        var swSqrt = Stopwatch.StartNew();
        GpuBigIntDivRem.Sqrt(gpuSqrtBase, gpuSqrtResult);
        swSqrt.Stop();
        gpuSqrtBase.Dispose();
        Console.WriteLine($"[GPU-Pi] Final division (Sqrt) computed in {swSqrt.ElapsedMilliseconds} ms");

        var swMul1 = Stopwatch.StartNew();
        using var gpuConstant = ToGpuBigInt(context, new BigInteger(426880), 0);
        var mulWords1 = gpuSqrtResult.NumWords + gpuConstant.NumWords;
        using var gpuSqrtWithConstant = new GpuBigInteger(context, mulWords1);
        GpuBigIntMath.Mul(gpuSqrtResult, gpuConstant, gpuSqrtWithConstant);
        swMul1.Stop();
        Console.WriteLine(
            $"[GPU-Pi] Final division (Mul1: Sqrt * 426880) computed in {swMul1.ElapsedMilliseconds} ms");

        var swMul2 = Stopwatch.StartNew();
        var mulWords2 = gpuSqrtWithConstant.NumWords + pqt.Q.NumWords;
        using var gpuNumeratorUnpadded = new GpuBigInteger(context, mulWords2);
        GpuBigIntMath.Mul(gpuSqrtWithConstant, pqt.Q, gpuNumeratorUnpadded);
        swMul2.Stop();
        Console.WriteLine($"[GPU-Pi] Final division (Mul2: * Q) computed in {swMul2.ElapsedMilliseconds} ms");

        var targetWords = Math.Max(gpuNumeratorUnpadded.NumWords, pqt.T.Value.NumWords);
        using var paddedNumerator = gpuNumeratorUnpadded.PadTo(targetWords);
        using var paddedT = pqt.T.Value.PadTo(targetWords);

        var gpuPiExt = new GpuBigInteger(context, targetWords);
        using var remainder = new GpuBigInteger(context, targetWords);

        var numBitsNumerator = paddedNumerator.GetBitLength();
        var numBitsT = paddedT.GetBitLength();

        // Execute Final Division entirely on GPU via Newton-Raphson NTT Division
        GpuBigIntDivRem.DivRem(paddedNumerator, paddedT, gpuPiExt, remainder);

        var finalDivTimeMs = finalStageStopwatch.ElapsedMilliseconds;
        Console.WriteLine($"[GPU-Pi] Final division (Newton-Raphson GPU NTT) computed in {finalDivTimeMs} ms");

        return gpuPiExt;
    }

    /// <summary>
    /// Scales the extended Pi fraction down by 10^extraDigits and outputs the decimal or hexadecimal digits,
    /// writing to a file or displaying an abbreviated preview on the console.
    /// </summary>
    private static void WritePiOutput(
        GpuContext context,
        GpuBigInteger gpuPiExt,
        int targetWords,
        int digits,
        int extraDigits,
        string? outputPath,
        bool asHex)
    {
        var radixStopwatch = Stopwatch.StartNew();
        var radixCache = new Dictionary<int, GpuBigInteger>();
        var activePointers = new List<GpuBigInteger>();

        try
        {
            if (asHex)
            {
                // Exact bits that are guaranteed to be correct (word-aligned)
                int exactBits = (int)Math.Floor(digits * 3.32192809489 / 32) * 32;
                int extraBits = 1024; // Safe margin for series truncation error
                int marginBits = 256; // Division noise margin
                
                var hostData = gpuPiExt.ToHostArray();
                
                // Find the actual integer part (the first non-zero word from the top)
                int actualTopIndex = hostData.Length - 1;
                while (actualTopIndex >= 0 && hostData[actualTopIndex] == 0)
                {
                    actualTopIndex--;
                }

                if (actualTopIndex < 0)
                {
                    actualTopIndex = 0;
                }
                
                int dropWords = (marginBits + extraBits) / 32;
                int truncationSafetyWords = 8; 
                
                int checkIndex = dropWords + truncationSafetyWords;
                
                if (checkIndex + 1 <= actualTopIndex)
                {
                    string computedHexTail = hostData[checkIndex + 1].ToString("X8") + hostData[checkIndex].ToString("X8");
                    int hexDigits = exactBits / 4;
                    int offset = hexDigits - 16 - (truncationSafetyWords * 8); // 8 hex digits per word
                    
                    PiVerification.VerifyWithBbp(computedHexTail, offset);
                }
                else
                {
                    Console.WriteLine("[Verification] FAIL: Not enough valid words to verify with BBP safety margin.");
                }

                using TextWriter hexWriter = outputPath != null
                    ? new StreamWriter(outputPath, append: false)
                    : new HeadTailTrackerWriter(50, 50);

                // Output integer part and fraction
                for (var i = actualTopIndex; i >= dropWords; i--)
                {
                    hexWriter.Write(hostData[i].ToString("X8"));
                }

                if (outputPath == null && hexWriter is HeadTailTrackerWriter ht)
                {
                    Console.WriteLine($"Result (Hex): {ht.GetPreview()}");
                    Console.WriteLine($"[GPU-Pi] Hex output tracked. Length: {ht.TotalCharactersWritten} chars.");
                }
                else
                {
                    Console.WriteLine($"[GPU-Pi] Hex output dumped to file.");
                }
            }
            else
            {
                // Remove extra digits from result by integer division: piExt = piExt / 10^extraDigits
                var extraDivisor = BigInteger.Pow(10, extraDigits);
                using var extraDivGpu = ToGpuBigInt(context, extraDivisor, targetWords);
                using var finalPi = new GpuBigInteger(context, targetWords);
                using var finalRem = new GpuBigInteger(context, targetWords);
                using var paddedPiExt = gpuPiExt.PadTo(targetWords);
                GpuBigIntDivRem.DivRem(paddedPiExt, extraDivGpu, finalPi, finalRem);

                using TextWriter writer = outputPath != null
                    ? new StreamWriter(outputPath, append: false)
                    : new HeadTailTrackerWriter(50, 50);

                ConvertAndWriteGpu(context, finalPi, digits + 1, writer, radixCache, activePointers,
                    isMostSignificant: true);

                if (outputPath == null && writer is HeadTailTrackerWriter htWriter)
                {
                    Console.WriteLine($"Result: {htWriter.GetPreview()}");
                }
                else if (outputPath != null)
                {
                    Console.WriteLine($"[GPU-Pi] Decimal output dumped to file.");
                }
            }
        }
        finally
        {
            foreach (var ptr in activePointers)
            {
                ptr.Dispose();
            }
        }

        radixStopwatch.Stop();
        Console.WriteLine($"[GPU-Pi] Base Conversion & Output: {radixStopwatch.ElapsedMilliseconds} ms");

        if (outputPath != null)
        {
            Console.WriteLine($"[GPU-Pi] Output successfully written to: {outputPath}");
        }
    }

    private static GpuBigInteger GpuPow10(GpuContext context, int exponent)
    {
        if (exponent <= 9)
        {
            return ToGpuBigInt(context, BigInteger.Pow(10, exponent), 0);
        }

        var resultGpu = ToGpuBigInt(context, 1, 0);
        var currentGpu = ToGpuBigInt(context, 10, 0);
        var exp = exponent;

        while (exp > 0)
        {
            if ((exp & 1) == 1)
            {
                var newResultWords = resultGpu.NumWords + currentGpu.NumWords;
                var nextResult = new GpuBigInteger(context, newResultWords);
                GpuBigIntMath.Mul(resultGpu, currentGpu, nextResult);

                var actualResultWords = (GpuBigIntDivRem.GetActualBits(nextResult) + 31) / 32;
                if (actualResultWords == 0) actualResultWords = 1;
                var trimmedResult = nextResult.PadTo(actualResultWords);
                nextResult.Dispose();

                resultGpu.Dispose();
                resultGpu = trimmedResult;
            }

            exp >>= 1;
            if (exp > 0)
            {
                var newCurrentWords = currentGpu.NumWords * 2;
                var nextCurrent = new GpuBigInteger(context, newCurrentWords);
                GpuBigIntMath.Mul(currentGpu, currentGpu, nextCurrent);

                var actualCurrentWords = (GpuBigIntDivRem.GetActualBits(nextCurrent) + 31) / 32;
                if (actualCurrentWords == 0) actualCurrentWords = 1;
                var trimmedCurrent = nextCurrent.PadTo(actualCurrentWords);
                nextCurrent.Dispose();

                currentGpu.Dispose();
                currentGpu = trimmedCurrent;
            }
        }

        currentGpu.Dispose();
        return resultGpu;
    }

    private static void ConvertAndWriteGpu(GpuContext context, GpuBigInteger value, int targetDigits,
        TextWriter writer, Dictionary<int, GpuBigInteger> radixCache, List<GpuBigInteger> activePointers,
        bool isMostSignificant)
    {
        var actualWords = (GpuBigIntDivRem.GetActualBits(value) + 31) / 32;
        if (actualWords == 0) actualWords = 1;

        // Leaf threshold: CPU formatting is fast enough for < 10,000 digits.
        if (targetDigits <= 10000)
        {
            var cpuVal = value.ToBigInteger();
            var str = cpuVal.ToString();

            if (!isMostSignificant && str.Length < targetDigits)
            {
                str = new string('0', targetDigits - str.Length) + str;
            }

            if (isMostSignificant && str.Length > 0)
            {
                str = str[..1] + "." + str[1..];
            }

            writer.Write(str);
            return;
        }

        var swDiv = Stopwatch.StartNew();

        var lowerDigits = targetDigits / 2;
        var upperDigits = targetDigits - lowerDigits;

        if (!radixCache.TryGetValue(lowerDigits, out var divisorGpu))
        {
            var swCache = Stopwatch.StartNew();
            if (lowerDigits > 1000000)
            {
                Console.WriteLine($"[GPU-Pi] Radix Cache: Computing 10^{lowerDigits} on GPU...");
            }

            divisorGpu = GpuPow10(context, lowerDigits);
            radixCache[lowerDigits] = divisorGpu;
            activePointers.Add(divisorGpu);
            swCache.Stop();

            if (lowerDigits > 1000000)
            {
                Console.WriteLine(
                    $"[GPU-Pi] Radix Cache: 10^{lowerDigits} computed in {swCache.ElapsedMilliseconds} ms");
            }
        }

        // Align dimensions for GPU division
        var targetWords = Math.Max(actualWords, divisorGpu.NumWords);
        using var paddedValue = value.PadTo(targetWords);
        using var paddedDivisor = divisorGpu.PadTo(targetWords);

        using var quotient = new GpuBigInteger(context, targetWords);
        using var remainder = new GpuBigInteger(context, targetWords);

        GpuBigIntDivRem.DivRem(paddedValue, paddedDivisor, quotient, remainder);
        swDiv.Stop();

        ConvertAndWriteGpu(context, quotient, upperDigits, writer, radixCache, activePointers, isMostSignificant);
        ConvertAndWriteGpu(context, remainder, lowerDigits, writer, radixCache, activePointers, false);
    }

    private static GpuPqt ComputeTree(int a, int b, GpuContext context, int thresholdTerms, ProgressState state)
    {
        var terms = b - a;
        if (terms <= thresholdTerms)
        {
            var cpuResult = ComputeCpuSubtree(a, b);
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
        var result = MergeGpu(context, left, right);
        swMerge.Stop();

        state.Report(b);
        return result;
    }

    private static GpuPqt MergeGpu(GpuContext context, GpuPqt left, GpuPqt right)
    {
        // P = Pl * Pr
        var pWords = left.P.Value.NumWords + right.P.Value.NumWords;
        var resultP = new GpuBigInteger(context, pWords);
        GpuBigIntMath.Mul(left.P.Value, right.P.Value, resultP);
        var pNegative = left.P.IsNegative ^ right.P.IsNegative;

        // Q = Ql * Qr
        var qWords = left.Q.NumWords + right.Q.NumWords;
        var resultQ = new GpuBigInteger(context, qWords);
        GpuBigIntMath.Mul(left.Q, right.Q, resultQ);

        // T = Tl * Qr + Pl * Tr
        var wordsA = left.T.Value.NumWords + right.Q.NumWords;
        using var rawTermA = new GpuBigInteger(context, wordsA);
        GpuBigIntMath.Mul(left.T.Value, right.Q, rawTermA);
        var signA = left.T.IsNegative;

        var wordsB = left.P.Value.NumWords + right.T.Value.NumWords;
        using var rawTermB = new GpuBigInteger(context, wordsB);
        GpuBigIntMath.Mul(left.P.Value, right.T.Value, rawTermB);
        var signB = left.P.IsNegative ^ right.T.IsNegative;

        var maxTWords = Math.Max(wordsA, wordsB);
        using var termA = rawTermA.PadTo(maxTWords);
        using var termB = rawTermB.PadTo(maxTWords);
        var resultT = new GpuBigInteger(context, maxTWords);

        bool tNegative;
        if (signA == signB)
        {
            GpuBigIntMath.Add(termA, termB, resultT);
            tNegative = signA;
        }
        else
        {
            var comparison = termA.CompareTo(termB);
            if (comparison >= 0)
            {
                GpuBigIntMath.Sub(termA, termB, resultT);
                tNegative = signA;
            }
            else
            {
                GpuBigIntMath.Sub(termB, termA, resultT);
                tNegative = signB;
            }
        }

        var result = new GpuPqt(
            new SignedGpuBigInteger(resultP, pNegative),
            resultQ,
            new SignedGpuBigInteger(resultT, tNegative)
        );
        return result;
    }

    private static CpuPqt ComputeCpuSubtree(int a, int b)
    {
        if (a + 1 == b)
        {
            if (a == 0)
            {
                return new CpuPqt { P = 1, Q = 1, T = 13591409 };
            }

            var p = -(BigInteger)(6L * a - 5) * (2L * a - 1) * (6L * a - 1);
            var q = (BigInteger)10939058860032000L * a * a * a;
            var t = p * (13591409 + (BigInteger)545140134L * a);

            return new CpuPqt { P = p, Q = q, T = t };
        }

        var m = (a + b) / 2;
        CpuPqt l = default, r = default;

        if (b - a >= 16)
        {
            Parallel.Invoke(
                () => l = ComputeCpuSubtree(a, m),
                () => r = ComputeCpuSubtree(m, b)
            );
        }
        else
        {
            l = ComputeCpuSubtree(a, m);
            r = ComputeCpuSubtree(m, b);
        }

        return new CpuPqt
        {
            P = l.P * r.P,
            Q = l.Q * r.Q,
            T = l.T * r.Q + l.P * r.T
        };
    }

    private static GpuPqt UploadToGpu(GpuContext context, CpuPqt cpu)
    {
        var sw = Stopwatch.StartNew();
        var pNeg = cpu.P < 0;
        var absP = BigInteger.Abs(cpu.P);
        var gpuP = GpuBigInteger.FromBigInteger(context, absP, 0);

        var gpuQ = GpuBigInteger.FromBigInteger(context, cpu.Q, 0);

        var tNeg = cpu.T < 0;
        var absT = BigInteger.Abs(cpu.T);
        var gpuT = GpuBigInteger.FromBigInteger(context, absT, 0);

        var result = new GpuPqt(
            new SignedGpuBigInteger(gpuP, pNeg),
            gpuQ,
            new SignedGpuBigInteger(gpuT, tNeg)
        );
        sw.Stop();
        // Console.WriteLine($"[GPU-Pi] UploadToGpu took {sw.ElapsedMilliseconds} ms"); // Trace if needed
        return result;
    }

    private static GpuBigInteger ToGpuBigInt(GpuContext context, BigInteger val, int minWords)
    {
        val = BigInteger.Abs(val);
        if (val.IsZero)
        {
            return new GpuBigInteger(context, new uint[Math.Max(1, minWords)]);
        }

        var byteCount = val.GetByteCount(isUnsigned: true);
        var words = (byteCount + 3) / 4;
        var allocWords = Math.Max(words, minWords);

        var hostData = new uint[allocWords];
        var byteSpan = MemoryMarshal.AsBytes(hostData.AsSpan());
        val.TryWriteBytes(byteSpan, out _, isUnsigned: true, isBigEndian: false);

        return new GpuBigInteger(context, hostData);
    }
}
