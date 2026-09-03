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

    /// <summary>
    /// Computes high-precision digits of Pi using the hybrid CPU/GPU Chudnovsky algorithm,
    /// executing the final division on GPU and streaming output directly to a file via D&C Radix Conversion.
    /// </summary>
    /// <param name="digits">Target number of decimal digits.</param>
    /// <param name="outputPath">Optional file path to write digits to.</param>
    /// <param name="thresholdTerms">Maximum number of terms evaluated on CPU before transitioning to GPU NTT merges.</param>
    public static void ComputePiAndWriteToFile(int digits, string? outputPath, int thresholdTerms = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);

        int totalTerms = digits / 14 + 1;
        const int extraDigits = 20;
        int totalDigits = digits + extraDigits;

        // Estimate max limb words for GPU workspace allocation
        int estimatedWords = (int)((totalDigits * 3.32192809489) / 32) + 256;
        int maxContextWords = Math.Max(4096, 1 << (int)Math.Ceiling(Math.Log2(estimatedWords * 4)));

        Console.WriteLine($"[GPU-Pi] Digits: {digits}, Terms: {totalTerms}, CPU/GPU Leaf Threshold: {thresholdTerms} terms");
        Console.WriteLine($"[GPU-Pi] Initializing CUDA Device & G# GPU Context (MaxWords: {maxContextWords:N0})...");

        CudaException.ThrowIfError(CudaDriverApi.cuInit(0));
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out int device, 0));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out IntPtr cudaContext, 0, device));

        try
        {
            using var context = new GpuContext(maxContextWords, GpuExecutionEngine.GSharpJit);

            var totalStopwatch = Stopwatch.StartNew();

            // 1. Compute Binary Splitting tree using Hybrid CPU/GPU execution
            using var pqt = ComputeTree(0, totalTerms, context, thresholdTerms);
            long treeTimeMs = totalStopwatch.ElapsedMilliseconds;
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
            Console.WriteLine($"[GPU-Pi] Final division (Mul1: Sqrt * 426880) computed in {swMul1.ElapsedMilliseconds} ms");

            var swMul2 = Stopwatch.StartNew();
            var mulWords2 = gpuSqrtWithConstant.NumWords + pqt.Q.NumWords;
            using var gpuNumeratorUnpadded = new GpuBigInteger(context, mulWords2);
            GpuBigIntMath.Mul(gpuSqrtWithConstant, pqt.Q, gpuNumeratorUnpadded);
            swMul2.Stop();
            Console.WriteLine($"[GPU-Pi] Final division (Mul2: * Q) computed in {swMul2.ElapsedMilliseconds} ms");

            int targetWords = Math.Max(gpuNumeratorUnpadded.NumWords, pqt.T.Value.NumWords);
            using var paddedNumerator = PadToSize(context, gpuNumeratorUnpadded, targetWords);
            using var paddedT = PadToSize(context, pqt.T.Value, targetWords);

            using var gpuPiExt = new GpuBigInteger(context, targetWords);
            using var remainder = new GpuBigInteger(context, targetWords);

            int numBitsNumerator = paddedNumerator.GetBitLength();
            int numBitsT = paddedT.GetBitLength();
            Console.WriteLine($"[DEBUG] Final DivRem inputs: Numerator words={paddedNumerator.NumWords}, ActualBits={numBitsNumerator}");
            Console.WriteLine($"[DEBUG] Final DivRem inputs: T words={paddedT.NumWords}, ActualBits={numBitsT}");
            
            var topN = paddedNumerator.GetTopWords(4);
            var topT = paddedT.GetTopWords(4);
            Console.WriteLine($"[DEBUG] Numerator Top 4 words: {topN[0]:X8} {topN[1]:X8} {topN[2]:X8} {topN[3]:X8}");
            Console.WriteLine($"[DEBUG] T Top 4 words: {topT[0]:X8} {topT[1]:X8} {topT[2]:X8} {topT[3]:X8}");

            // Execute Final Division entirely on GPU via Newton-Raphson NTT Division
            GpuBigIntDivRem.DivRem(paddedNumerator, paddedT, gpuPiExt, remainder);

            long finalDivTimeMs = finalStageStopwatch.ElapsedMilliseconds;
            Console.WriteLine($"[GPU-Pi] Final division (Newton-Raphson GPU NTT) computed in {finalDivTimeMs} ms");

            // 3. Fast D&C Radix Conversion & Streaming Output
            var radixStopwatch = Stopwatch.StartNew();
            using StreamWriter? writer = outputPath != null ? new StreamWriter(outputPath, append: false) : null;
            var radixCache = new Dictionary<int, GpuBigInteger>();
            var activePointers = new List<GpuBigInteger>(); // Track dynamically allocated cached bigints to dispose later

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
                ConvertAndWriteGpu(context, finalPi, digits + 1, writer, radixCache, activePointers, isMostSignificant: true);

                if (writer == null) Console.WriteLine();
            }
            finally
            {
                foreach (var ptr in activePointers) ptr.Dispose();
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
        StreamWriter? writer, Dictionary<int, GpuBigInteger> radixCache, List<GpuBigInteger> activePointers, bool isMostSignificant)
    {
        // Leaf threshold: CPU formatting is fast enough for < 10,000 digits.
        if (targetDigits <= 10000)
        {
            var swCpu = Stopwatch.StartNew();
            var cpuVal = ToBigInteger(value.ToHostArray(), isNegative: false);
            string str = cpuVal.ToString();

            if (!isMostSignificant && str.Length < targetDigits)
            {
                str = new string('0', targetDigits - str.Length) + str;
            }

            if (isMostSignificant && str.Length > 0)
            {
                str = str.Substring(0, 1) + "." + str.Substring(1);
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
            Console.WriteLine($"[GPU-Pi] Base Conversion Leaf ({targetDigits} digits): ToHost+ToString+Write took {swCpu.ElapsedMilliseconds} ms");
            return;
        }

        var swDiv = Stopwatch.StartNew();

        int lowerDigits = targetDigits / 2;
        int upperDigits = targetDigits - lowerDigits;

        if (!radixCache.TryGetValue(lowerDigits, out var divisorGpu))
        {
            var swCache = Stopwatch.StartNew();
            var divisorCpu = BigInteger.Pow(10, lowerDigits);
            divisorGpu = ToGpuBigInt(context, divisorCpu, value.NumWords);
            radixCache[lowerDigits] = divisorGpu;
            activePointers.Add(divisorGpu);
            swCache.Stop();
            // Console.WriteLine($"[GPU-Pi] Base Conversion RadixCache ({lowerDigits} digits): BigInt Pow+Upload took {swCache.ElapsedMilliseconds} ms");
        }

        // Align dimensions for GPU division
        int targetWords = Math.Max(value.NumWords, divisorGpu.NumWords);
        using var paddedValue = PadToSize(context, value, targetWords);
        using var paddedDivisor = PadToSize(context, divisorGpu, targetWords);

        using var quotient = new GpuBigInteger(context, targetWords);
        using var remainder = new GpuBigInteger(context, targetWords);

        GpuBigIntDivRem.DivRem(paddedValue, paddedDivisor, quotient, remainder);
        swDiv.Stop();
        Console.WriteLine($"[GPU-Pi] Base Conversion DivRem ({targetDigits} / {lowerDigits} digits): took {swDiv.ElapsedMilliseconds} ms");

        ConvertAndWriteGpu(context, quotient, upperDigits, writer, radixCache, activePointers, isMostSignificant);
        ConvertAndWriteGpu(context, remainder, lowerDigits, writer, radixCache, activePointers, false);
    }

    private static GpuPqt ComputeTree(int a, int b, GpuContext context, int thresholdTerms)
    {
        int terms = b - a;
        if (terms <= thresholdTerms)
        {
            var cpuResult = ComputeCpuSubtree(a, b);
            return UploadToGpu(context, cpuResult);
        }

        int m = (a + b) / 2;
        var swLeft = Stopwatch.StartNew();
        using var left = ComputeTree(a, m, context, thresholdTerms);
        swLeft.Stop();
        var swRight = Stopwatch.StartNew();
        using var right = ComputeTree(m, b, context, thresholdTerms);
        swRight.Stop();

        var swMerge = Stopwatch.StartNew();
        var result = MergeGpu(context, left, right);
        swMerge.Stop();
        
        if (terms > thresholdTerms * 16)
        {
            Console.WriteLine($"[GPU-Pi] Subtree({a},{b}): Left={swLeft.ElapsedMilliseconds}ms, Right={swRight.ElapsedMilliseconds}ms, Merge={swMerge.ElapsedMilliseconds}ms");
        }
        
        return result;
    }

    private static GpuPqt MergeGpu(GpuContext context, GpuPqt left, GpuPqt right)
    {
        // P = Pl * Pr
        int pWords = left.P.Value.NumWords + right.P.Value.NumWords;
        var resultP = new GpuBigInteger(context, pWords);
        GpuBigIntMath.Mul(left.P.Value, right.P.Value, resultP);
        bool pNegative = left.P.IsNegative ^ right.P.IsNegative;

        // Q = Ql * Qr
        int qWords = left.Q.NumWords + right.Q.NumWords;
        var resultQ = new GpuBigInteger(context, qWords);
        GpuBigIntMath.Mul(left.Q, right.Q, resultQ);

        // T = Tl * Qr + Pl * Tr
        int wordsA = left.T.Value.NumWords + right.Q.NumWords;
        using var rawTermA = new GpuBigInteger(context, wordsA);
        GpuBigIntMath.Mul(left.T.Value, right.Q, rawTermA);
        bool signA = left.T.IsNegative;

        int wordsB = left.P.Value.NumWords + right.T.Value.NumWords;
        using var rawTermB = new GpuBigInteger(context, wordsB);
        GpuBigIntMath.Mul(left.P.Value, right.T.Value, rawTermB);
        bool signB = left.P.IsNegative ^ right.T.IsNegative;

        int maxTWords = Math.Max(wordsA, wordsB);
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
            int comparison = termA.CompareTo(termB);
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
        CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(padded.DevicePointer, 0, (IntPtr)(targetWords * sizeof(uint))));
        context.Provider.CopyDeviceToDevice(padded.DevicePointer, src.DevicePointer, Math.Min(src.NumWords, targetWords));
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

            BigInteger p = -(BigInteger)(6L * a - 5) * (2L * a - 1) * (6L * a - 1);
            BigInteger q = (BigInteger)10939058860032000L * a * a * a;
            BigInteger t = p * (13591409 + (BigInteger)545140134L * a);

            return new CpuPqt { P = p, Q = q, T = t };
        }

        int m = (a + b) / 2;
        var l = ComputeCpuSubtree(a, m);
        var r = ComputeCpuSubtree(m, b);

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
        bool pNeg = cpu.P < 0;
        var absP = BigInteger.Abs(cpu.P);
        var gpuP = ToGpuBigInt(context, absP, 0);

        var gpuQ = ToGpuBigInt(context, cpu.Q, 0);

        bool tNeg = cpu.T < 0;
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

        int byteCount = val.GetByteCount(isUnsigned: true);
        int words = (byteCount + 3) / 4;
        int allocWords = Math.Max(words, minWords);

        uint[] hostData = new uint[allocWords];
        Span<byte> byteSpan = MemoryMarshal.AsBytes(hostData.AsSpan());
        val.TryWriteBytes(byteSpan, out _, isUnsigned: true, isBigEndian: false);

        return new GpuBigInteger(context, hostData);
    }

    private static BigInteger ToBigInteger(ReadOnlySpan<uint> words, bool isNegative)
    {
        ReadOnlySpan<byte> byteSpan = MemoryMarshal.AsBytes(words);
        var big = new BigInteger(byteSpan, isUnsigned: true, isBigEndian: false);
        return isNegative ? -big : big;
    }
}




