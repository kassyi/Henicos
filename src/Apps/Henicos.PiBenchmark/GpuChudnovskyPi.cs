using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Henicos.PiBenchmark;

/// <summary>
/// GPU-accelerated Chudnovsky binary splitting algorithm for ultra-fast high-precision Pi computation.
/// Utilizes G# <see cref="GpuBigInteger"/> and NTT multiplication kernels with Zero-PCIe intermediate merges.
/// </summary>
public static class GpuChudnovskyPi
{
    private readonly struct SignedGpuBigInt : IDisposable
    {
        public GpuBigInteger Value { get; }
        public bool IsNegative { get; }

        public SignedGpuBigInt(GpuBigInteger value, bool isNegative)
        {
            Value = value;
            IsNegative = isNegative;
        }

        public void Dispose() => Value.Dispose();
    }

    private readonly struct GpuPqt : IDisposable
    {
        public SignedGpuBigInt P { get; }
        public GpuBigInteger Q { get; }
        public SignedGpuBigInt T { get; }

        public GpuPqt(SignedGpuBigInt p, GpuBigInteger q, SignedGpuBigInt t)
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

            if (currentB == TotalTerms)
            {
                Console.WriteLine();
            }
        }
    }

    /// <summary>
    /// Computes high-precision digits of Pi using the hybrid CPU/GPU Chudnovsky algorithm,
    /// executing the final division on GPU and streaming output directly to a file via D&C Radix Conversion.
    /// </summary>
    /// <param name="digits">Target number of decimal digits.</param>
    /// <param name="outputPath">Optional file path to write digits to.</param>
    /// <param name="thresholdTerms">Maximum number of terms evaluated on CPU before transitioning to GPU NTT merges.</param>
    public static void ComputePiAndWriteToFile(int digits, string? outputPath, int thresholdTerms = 8192,
        bool asHex = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);

        var totalTerms = digits / 14 + 1;
        const int extraDigits = 20;
        var totalDigits = digits + extraDigits;

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
            Console.WriteLine($"[GPU-Pi] Binary Splitting Tree computed in {treeTimeMs} ms");

            // 2. Final stage: Pi = (426880 * Sqrt(10005 * 10^(2*totalDigits)) * Q) / T
            var finalStageStopwatch = Stopwatch.StartNew();

            var sqrtBase = 10005 * BigInteger.Pow(10, 2 * totalDigits);
            using var gpuSqrtBase = ToGpuBigInt(context, sqrtBase, 0);
            using var gpuSqrtResult = new GpuBigInteger(context, gpuSqrtBase.NumWords);
            var swSqrt = Stopwatch.StartNew();
            GpuBigIntDivRem.Sqrt(gpuSqrtBase, gpuSqrtResult);
            swSqrt.Stop();
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
            using var paddedNumerator = PadToSize(context, gpuNumeratorUnpadded, targetWords);
            using var paddedT = PadToSize(context, pqt.T.Value, targetWords);

            using var gpuPiExt = new GpuBigInteger(context, targetWords);
            using var remainder = new GpuBigInteger(context, targetWords);

            var numBitsNumerator = paddedNumerator.GetBitLength();
            var numBitsT = paddedT.GetBitLength();
            Console.WriteLine(
                $"[DEBUG] Final DivRem inputs: Numerator words={paddedNumerator.NumWords}, ActualBits={numBitsNumerator}");
            Console.WriteLine($"[DEBUG] Final DivRem inputs: T words={paddedT.NumWords}, ActualBits={numBitsT}");

            var topN = paddedNumerator.GetTopWords(4);
            var topT = paddedT.GetTopWords(4);
            Console.WriteLine($"[DEBUG] Numerator Top 4 words: {topN[0]:X8} {topN[1]:X8} {topN[2]:X8} {topN[3]:X8}");
            Console.WriteLine($"[DEBUG] T Top 4 words: {topT[0]:X8} {topT[1]:X8} {topT[2]:X8} {topT[3]:X8}");

            // Execute Final Division entirely on GPU via Newton-Raphson NTT Division
            GpuBigIntDivRem.DivRem(paddedNumerator, paddedT, gpuPiExt, remainder);

            var finalDivTimeMs = finalStageStopwatch.ElapsedMilliseconds;
            Console.WriteLine($"[GPU-Pi] Final division (Newton-Raphson GPU NTT) computed in {finalDivTimeMs} ms");

            // 3. Fast D&C Radix Conversion & Streaming Output
            var radixStopwatch = Stopwatch.StartNew();
            using var writer = outputPath != null ? new StreamWriter(outputPath, append: false) : null;
            var radixCache = new Dictionary<int, GpuBigInteger>();
            var activePointers =
                new List<GpuBigInteger>(); // Track dynamically allocated cached bigints to dispose later

            try
            {
                // Remove extra digits from result by integer division: piExt = piExt / 10^extraDigits
                var extraDivisor = BigInteger.Pow(10, extraDigits);
                using var extraDivGpu = ToGpuBigInt(context, extraDivisor, targetWords);
                using var finalPi = new GpuBigInteger(context, targetWords);
                using var finalRem = new GpuBigInteger(context, targetWords);
                using var paddedPiExt = PadToSize(context, gpuPiExt, targetWords);
                GpuBigIntDivRem.DivRem(paddedPiExt, extraDivGpu, finalPi, finalRem);

                // Fast recursive base conversion (D&C)
                if (asHex)
                {
                    var hostData = finalPi.ToHostArray();
                    var sb = new System.Text.StringBuilder(hostData.Length * 8);
                    for (var i = hostData.Length - 1; i >= 0; i--)
                    {
                        sb.Append(hostData[i].ToString("X8"));
                    }

                    var hexStr = sb.ToString();
                    writer?.WriteLine(hexStr);
                    Console.WriteLine($"[GPU-Pi] Hex output dumped. Length: {hexStr.Length} chars.");
                }
                else
                {
                    ConvertAndWriteGpu(context, finalPi, digits + 1, writer, radixCache, activePointers,
                        isMostSignificant: true);
                }

                if (writer == null)
                {
                    Console.WriteLine();
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
            finalStageStopwatch.Stop();
            totalStopwatch.Stop();

            Console.WriteLine(
                $"[GPU-Pi] Base Conversion & Output: {radixStopwatch.ElapsedMilliseconds} ms, Total: {totalStopwatch.ElapsedMilliseconds} ms");

            if (outputPath != null)
            {
                Console.WriteLine($"[GPU-Pi] Output successfully written to: {outputPath}");
            }
        }
        finally
        {
            CudaDriverApi.cuCtxDestroy_v2(cudaContext);
        }
    }

    private static void ConvertAndWriteGpu(GpuContext context, GpuBigInteger value, int targetDigits,
        StreamWriter? writer, Dictionary<int, GpuBigInteger> radixCache, List<GpuBigInteger> activePointers,
        bool isMostSignificant)
    {
        var actualWords = (GpuBigIntDivRem.GetActualBits(value) + 31) / 32;
        if (actualWords == 0) actualWords = 1;

        // Leaf threshold: CPU formatting is fast enough for < 10,000 digits.
        if (targetDigits <= 10000)
        {
            var swCpu = Stopwatch.StartNew();
            var hostArray = new uint[actualWords];
            context.Provider.CopyDeviceToHost(hostArray, value.DevicePointer, actualWords);
            var cpuVal = ToBigInteger(hostArray, isNegative: false);
            var str = cpuVal.ToString();

            if (!isMostSignificant && str.Length < targetDigits)
            {
                str = new string('0', targetDigits - str.Length) + str;
            }

            if (isMostSignificant && str.Length > 0)
            {
                str = str[..1] + "." + str[1..];
            }

            if (writer != null)
            {
                writer.Write(str);
            }
            else
            {
                Console.Write(str);
            }

            swCpu.Stop();
            Console.WriteLine(
                $"[GPU-Pi] Base Conversion Leaf ({targetDigits} digits): ToHost+ToString+Write took {swCpu.ElapsedMilliseconds} ms");
            return;
        }

        var swDiv = Stopwatch.StartNew();

        var lowerDigits = targetDigits / 2;
        var upperDigits = targetDigits - lowerDigits;

        if (!radixCache.TryGetValue(lowerDigits, out var divisorGpu))
        {
            var swCache = Stopwatch.StartNew();
            var divisorCpu = BigInteger.Pow(10, lowerDigits);
            var divWords = (int)Math.Ceiling(divisorCpu.GetBitLength() / 32.0);
            if (divWords == 0)
            {
                divWords = 1;
            }

            divisorGpu = ToGpuBigInt(context, divisorCpu, divWords);
            radixCache[lowerDigits] = divisorGpu;
            activePointers.Add(divisorGpu);
            swCache.Stop();
            // Console.WriteLine($"[GPU-Pi] Base Conversion RadixCache ({lowerDigits} digits): BigInt Pow+Upload took {swCache.ElapsedMilliseconds} ms");
        }

        // Align dimensions for GPU division
        var targetWords = Math.Max(actualWords, divisorGpu.NumWords);
        using var paddedValue = PadToSize(context, value, targetWords);
        using var paddedDivisor = PadToSize(context, divisorGpu, targetWords);

        using var quotient = new GpuBigInteger(context, targetWords);
        using var remainder = new GpuBigInteger(context, targetWords);

        GpuBigIntDivRem.DivRem(paddedValue, paddedDivisor, quotient, remainder);
        swDiv.Stop();
        Console.WriteLine(
            $"[GPU-Pi] Base Conversion DivRem ({targetDigits} / {lowerDigits} digits): took {swDiv.ElapsedMilliseconds} ms");

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
        using var termA = PadToSize(context, rawTermA, maxTWords);
        using var termB = PadToSize(context, rawTermB, maxTWords);
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
            new SignedGpuBigInt(resultP, pNegative),
            resultQ,
            new SignedGpuBigInt(resultT, tNegative)
        );
        return result;
    }

    private static GpuBigInteger PadToSize(GpuContext context, GpuBigInteger src, int targetWords)
    {
        if (src.NumWords == targetWords)
        {
            var copy = new GpuBigInteger(context, targetWords);
            context.Provider.CopyDeviceToDevice(copy.DevicePointer, src.DevicePointer, targetWords);
            return copy;
        }

        var padded = new GpuBigInteger(context, targetWords);
        CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(padded.DevicePointer, 0,
            targetWords * sizeof(uint)));
        context.Provider.CopyDeviceToDevice(padded.DevicePointer, src.DevicePointer,
            Math.Min(src.NumWords, targetWords));
        return padded;
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
        var gpuP = ToGpuBigInt(context, absP, 0);

        var gpuQ = ToGpuBigInt(context, cpu.Q, 0);

        var tNeg = cpu.T < 0;
        var absT = BigInteger.Abs(cpu.T);
        var gpuT = ToGpuBigInt(context, absT, 0);

        var result = new GpuPqt(
            new SignedGpuBigInt(gpuP, pNeg),
            gpuQ,
            new SignedGpuBigInt(gpuT, tNeg)
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

    private static BigInteger ToBigInteger(ReadOnlySpan<uint> words, bool isNegative)
    {
        var byteSpan = MemoryMarshal.AsBytes(words);
        var big = new BigInteger(byteSpan, isUnsigned: true, isBigEndian: false);
        return isNegative ? -big : big;
    }
}





