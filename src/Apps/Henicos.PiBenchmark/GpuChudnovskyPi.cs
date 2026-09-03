using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.Math;

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
    /// Computes high-precision digits of Pi using the hybrid CPU/GPU Chudnovsky algorithm.
    /// </summary>
    /// <param name="digits">Target number of decimal digits.</param>
    /// <param name="thresholdTerms">Maximum number of terms evaluated on CPU before transitioning to GPU NTT merges.</param>
    /// <returns>A string containing the computed decimal digits of Pi.</returns>
    public static string ComputePi(int digits, int thresholdTerms = 16)
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
            var sqrt10005Cpu = SqrtCpu(sqrtBase);

            var qHostWords = pqt.Q.ToHostArray();
            var qBigInt = ToBigInteger(qHostWords, isNegative: false);

            var tHostWords = pqt.T.Value.ToHostArray();
            var tBigInt = ToBigInteger(tHostWords, pqt.T.IsNegative);

            var numerator = (BigInteger)426880 * sqrt10005Cpu * qBigInt;
            var piExt = numerator / tBigInt;
            var pi = piExt / BigInteger.Pow(10, extraDigits);

            finalStageStopwatch.Stop();
            totalStopwatch.Stop();

            Console.WriteLine(
                $"[GPU-Pi] Final division/assembly: {finalStageStopwatch.ElapsedMilliseconds} ms, Total: {totalStopwatch.ElapsedMilliseconds} ms");

            return pi.ToString();
        }
        finally
        {
            CudaDriverApi.cuCtxDestroy_v2(cudaContext);
        }
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
        using var left = ComputeTree(a, m, context, thresholdTerms);
        using var right = ComputeTree(m, b, context, thresholdTerms);

        return MergeGpu(context, left, right);
    }

    private static GpuPqt MergeGpu(GpuContext context, GpuPqt left, GpuPqt right)
    {
        // P = Pl * Pr
        int pWords = left.P.Value.NumWords + right.P.Value.NumWords;
        var resultP = new GpuBigInteger(context, pWords);
        GpuBigInteger.Mul(left.P.Value, right.P.Value, resultP);
        bool pNegative = left.P.IsNegative ^ right.P.IsNegative;

        // Q = Ql * Qr
        int qWords = left.Q.NumWords + right.Q.NumWords;
        var resultQ = new GpuBigInteger(context, qWords);
        GpuBigInteger.Mul(left.Q, right.Q, resultQ);

        // T = Tl * Qr + Pl * Tr
        int wordsA = left.T.Value.NumWords + right.Q.NumWords;
        using var rawTermA = new GpuBigInteger(context, wordsA);
        GpuBigInteger.Mul(left.T.Value, right.Q, rawTermA);
        bool signA = left.T.IsNegative;

        int wordsB = left.P.Value.NumWords + right.T.Value.NumWords;
        using var rawTermB = new GpuBigInteger(context, wordsB);
        GpuBigInteger.Mul(left.P.Value, right.T.Value, rawTermB);
        bool signB = left.P.IsNegative ^ right.T.IsNegative;

        int maxTWords = Math.Max(wordsA, wordsB);
        using var termA = PadToSize(context, rawTermA, maxTWords);
        using var termB = PadToSize(context, rawTermB, maxTWords);
        var resultT = new GpuBigInteger(context, maxTWords);

        bool tNegative;
        if (signA == signB)
        {
            GpuBigInteger.Add(termA, termB, resultT);
            tNegative = signA;
        }
        else
        {
            int comparison = termA.CompareTo(termB);
            if (comparison >= 0)
            {
                GpuBigInteger.Sub(termA, termB, resultT);
                tNegative = signA;
            }
            else
            {
                GpuBigInteger.Sub(termB, termA, resultT);
                tNegative = signB;
            }
        }

        return new GpuPqt(
            new SignedGpuBigInt(resultP, pNegative),
            resultQ,
            new SignedGpuBigInt(resultT, tNegative)
        );
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
        bool pNeg = cpu.P < 0;
        var absP = BigInteger.Abs(cpu.P);
        var gpuP = ToGpuBigInt(context, absP, 0);

        var gpuQ = ToGpuBigInt(context, cpu.Q, 0);

        bool tNeg = cpu.T < 0;
        var absT = BigInteger.Abs(cpu.T);
        var gpuT = ToGpuBigInt(context, absT, 0);

        return new GpuPqt(
            new SignedGpuBigInt(gpuP, pNeg),
            gpuQ,
            new SignedGpuBigInt(gpuT, tNeg)
        );
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

    private static BigInteger SqrtCpu(BigInteger n)
    {
        if (n == 0)
        {
            return 0;
        }

        if (n > 0)
        {
            int bitLength = (int)Math.Ceiling(BigInteger.Log(n, 2));
            var root = BigInteger.One << (bitLength / 2);

            while (true)
            {
                var next = (root + n / root) >> 1;
                if (next == root || next == root + 1)
                {
                    if (root * root > n)
                    {
                        return root - 1;
                    }

                    return root;
                }

                root = next;
            }
        }

        throw new ArithmeticException("Cannot compute square root of a negative number.");
    }
}
