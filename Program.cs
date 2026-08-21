using System;
using System.Diagnostics;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Henicos;

[MarkdownExporterAttribute.GitHub]
public class FermatBenchmark
{
    [Params(10, 50, 100, 500, 1000, 2000, 5000, 10000)]
    public int E { get; set; }

    private BigInteger X;
    private BigInteger N;
    private BigInteger a;
    private BigInteger exponent;

    [GlobalSetup]
    public void Setup()
    {
        X = BigInteger.One << E;
        N = (BigInteger.One << (2 * E + 1)) - X + 11;
        a = 2;
        exponent = N - 1;
    }

    [Benchmark(Baseline = true)]
    public BigInteger StandardBigInteger()
    {
        return BigInteger.ModPow(a, exponent, N);
    }

    [Benchmark]
    public BigInteger CustomReductionV1()
    {
        return ModularReduction.ModPowCustom(a, exponent, E, N);
    }

    [Benchmark]
    public BigInteger UltraOptimizedV2()
    {
        return ModularReduction2.FermatPRPTest(E, N);
    }
}

class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--benchmark")
        {
            var summary = BenchmarkRunner.Run<FermatBenchmark>();
            return;
        }

        Console.WriteLine("Henicos Project - Ultra Optimized Modular Reduction Benchmark");
        Console.WriteLine("---------------------------------------------------------------");
        Console.WriteLine("To run formal benchmarks, pass '--benchmark' argument.");
        Console.WriteLine("---------------------------------------------------------------");

        // 比較対象の E
        // E=1,2,3 は最適化版でも制約に引っかかるか、静的展開の固定ビットの前提 (E >= 4) が崩れるため除外
        int[] testEs = { 10, 50, 100, 500, 1000, 2000, 5000, 10000 };
        foreach (var E in testEs)
        {
            RunBenchmarkForE(E);
        }
    }

    static void RunBenchmarkForE(int E)
    {
        Console.WriteLine($"\n[ Benchmarking for E = {E} ]");

        BigInteger X = BigInteger.One << E;
        BigInteger N = (BigInteger.One << (2 * E + 1)) - X + 11;
        BigInteger a = 2;
        BigInteger exponent = N - 1;

        // Validation for HybridReduction
        bool hybridPass = false;
        if (E <= 10000)
        {
            hybridPass = TestHybridReduction(E, N);
        }

        // 1. Standard BigInteger.ModPow
        Stopwatch swStandard = Stopwatch.StartNew();
        BigInteger standardResult = BigInteger.ModPow(a, exponent, N);
        swStandard.Stop();

        // 2. Custom (Phase 1 Baseline)
        Stopwatch swCustom1 = Stopwatch.StartNew();
        BigInteger custom1Result = ModularReduction.ModPowCustom(a, exponent, E, N);
        swCustom1.Stop();

        // 3. Ultra Optimized (Phase 1.5 Breakthrough)
        Stopwatch swCustom2 = Stopwatch.StartNew();
        BigInteger custom2Result = ModularReduction2.FermatPRPTest(E, N);
        swCustom2.Stop();

        // 4. Hybrid Reduction V3 (Timing only the reduction step, simulating CPU offload)
        int numWords = (2 * E + 1 + 31) / 32;
        BigInteger T = BigInteger.Pow(X, 2) + 123456789;
        byte[] tBytes = T.ToByteArray();
        uint[] data = new uint[numWords * 4];
        Buffer.BlockCopy(tBytes, 0, data, 0, tBytes.Length);
        
        Stopwatch swCustom3 = Stopwatch.StartNew();
        int hybridIters = (E <= 1000) ? 10000 : 100;
        for (int i = 0; i < hybridIters; i++)
        {
            HybridReduction.ReduceInPlace(data, E, numWords);
        }
        swCustom3.Stop();
        double hybridMsPerIter = (double)swCustom3.ElapsedMilliseconds / hybridIters;

        // 5. Full Hybrid CPU-GPU Pipeline (V4)
        BigInteger custom4Result = BigInteger.Zero;
        Stopwatch swCustom4 = new Stopwatch();
        bool gpuInit = CudaInterop.InitializeGPU(E);
        if (gpuInit)
        {
            // Initial state: X = 2
            uint[] hybridData = new uint[numWords * 4];
            hybridData[0] = 2;
            
            // Precalculate N for normalization
            uint[] n_arr = new uint[numWords];
            int bit2E1 = 2 * E + 1;
            int rem2E1 = bit2E1 % 32;
            int word2E1 = bit2E1 / 32;
            if (word2E1 < numWords) n_arr[word2E1] |= (1U << rem2E1);
            n_arr[0] += 11;

            int bitE = E;
            int remE = bitE % 32;
            int wordE = bitE / 32;
            uint borrow = 0;
            for (int i = 0; i < numWords; i++)
            {
                uint sub = (i == wordE) ? (1U << remE) : 0;
                long temp = (long)n_arr[i] - sub - borrow;
                if (temp < 0) { borrow = 1; temp += 0x100000000; } else { borrow = 0; }
                n_arr[i] = (uint)temp;
            }

            swCustom4.Start();
            
            // Define a helper action for one step of "Square and Reduce"
            Action SquareAndReduce = () =>
            {
                CudaInterop.MultiplySquare(hybridData, numWords);
                HybridReduction.ReduceInPlace(hybridData, E, numWords);
                
                // Normalization
                if ((hybridData[numWords - 1] & 0x80000000) != 0)
                {
                    uint carry = 0;
                    for (int j = 0; j < numWords; j++)
                    {
                        ulong sum = (ulong)hybridData[j] + n_arr[j] + carry;
                        hybridData[j] = (uint)sum;
                        carry = (uint)(sum >> 32);
                    }
                }
                for (int loop = 0; loop < 2; loop++)
                {
                    bool geq = false;
                    for (int j = numWords - 1; j >= 0; j--)
                    {
                        if (hybridData[j] > n_arr[j]) { geq = true; break; }
                        if (hybridData[j] < n_arr[j]) { break; }
                        if (j == 0) geq = true;
                    }
                    if (geq)
                    {
                        uint borrowSub = 0;
                        for (int j = 0; j < numWords; j++)
                        {
                            long temp = (long)hybridData[j] - n_arr[j] - borrowSub;
                            if (temp < 0) { borrowSub = 1; temp += 0x100000000; } else { borrowSub = 0; }
                            hybridData[j] = (uint)temp;
                        }
                    }
                }
                hybridData[numWords - 1] &= (rem2E1 == 0) ? 0xFFFFFFFFU : ((1U << rem2E1) - 1);
            };

            // Define a helper action for "Multiply by 2 and Reduce"
            Action MultiplyBy2AndReduce = () =>
            {
                uint carry = 0;
                for (int j = 0; j < numWords; j++)
                {
                    uint next_carry = hybridData[j] >> 31;
                    hybridData[j] = (hybridData[j] << 1) | carry;
                    carry = next_carry;
                }
                
                // Normalization (sub N if >= N)
                for (int loop = 0; loop < 2; loop++)
                {
                    bool geq = false;
                    for (int j = numWords - 1; j >= 0; j--)
                    {
                        if (hybridData[j] > n_arr[j]) { geq = true; break; }
                        if (hybridData[j] < n_arr[j]) { break; }
                        if (j == 0) geq = true;
                    }
                    if (geq || carry > 0)
                    {
                        uint borrowSub = 0;
                        for (int j = 0; j < numWords; j++)
                        {
                            long temp = (long)hybridData[j] - n_arr[j] - borrowSub;
                            if (temp < 0) { borrowSub = 1; temp += 0x100000000; } else { borrowSub = 0; }
                            hybridData[j] = (uint)temp;
                        }
                        carry = 0; // consumed
                    }
                }
                hybridData[numWords - 1] &= (rem2E1 == 0) ? 0xFFFFFFFFU : ((1U << rem2E1) - 1);
            };

            // Fermat PRP Loop (N-1)
            for (int i = 0; i < E - 4; i++) { SquareAndReduce(); }
            SquareAndReduce(); MultiplyBy2AndReduce();
            SquareAndReduce();
            SquareAndReduce(); MultiplyBy2AndReduce();
            SquareAndReduce();
            
            // Final adjustments
            // (n_arr is already calculated above)

            for (int k = 0; k < 10; k++)
            {
                bool geq = false;
                for (int j = numWords - 1; j >= 0; j--)
                {
                    if (hybridData[j] > n_arr[j]) { geq = true; break; }
                    if (hybridData[j] < n_arr[j]) { break; }
                    if (j == 0) geq = true;
                }
                if (!geq) break;
                
                uint borrowFinal = 0;
                for (int j = 0; j < numWords; j++)
                {
                    long temp = (long)hybridData[j] - n_arr[j] - borrowFinal;
                    if (temp < 0) { borrowFinal = 1; temp += 0x100000000; } else { borrowFinal = 0; }
                    hybridData[j] = (uint)temp;
                }
            }
            swCustom4.Stop();
            CudaInterop.CleanupGPU();
            
            // Convert to BigInteger for comparison
            byte[] resBytes = new byte[numWords * 4 + 1];
            Buffer.BlockCopy(hybridData, 0, resBytes, 0, numWords * 4);
            resBytes[numWords * 4] = 0; // force positive
            custom4Result = new BigInteger(resBytes);
        }

        // 6. Zero-Overhead VRAM Loop (V5)
        BigInteger custom5Result = BigInteger.Zero;
        Stopwatch swCustom5 = new Stopwatch();
        bool gpuInitV5 = CudaInterop.InitializeGPU(E);
        if (gpuInitV5)
        {
            uint[] v5Data = new uint[numWords * 4];
            v5Data[0] = 2; // Initial state: X = 2

            swCustom5.Start();
            CudaInterop.RunFermatLoopV5(v5Data, numWords, E);
            swCustom5.Stop();

            CudaInterop.CleanupGPU();

            byte[] resBytes = new byte[numWords * 4 + 1];
            Buffer.BlockCopy(v5Data, 0, resBytes, 0, numWords * 4);
            resBytes[numWords * 4] = 0;
            custom5Result = new BigInteger(resBytes);
        }

        // Check Correctness
        bool pass1 = (standardResult == custom1Result);
        bool pass2 = (standardResult == custom2Result);
        bool pass4 = (standardResult == custom4Result);
        bool pass5 = (standardResult == custom5Result);

        // Calculate M-Digits/sec
        double digitsPerLoop = (2 * E + 1) * Math.Log10(2);
        double totalDigits = digitsPerLoop * E; 
        
        double stdMDps = (swStandard.ElapsedMilliseconds > 0) ? (totalDigits / swStandard.ElapsedMilliseconds) / 1000.0 : 0;
        double v1MDps = (swCustom1.ElapsedMilliseconds > 0) ? (totalDigits / swCustom1.ElapsedMilliseconds) / 1000.0 : 0;
        double v2MDps = (swCustom2.ElapsedMilliseconds > 0) ? (totalDigits / swCustom2.ElapsedMilliseconds) / 1000.0 : 0;
        double v4MDps = (swCustom4.ElapsedMilliseconds > 0) ? (totalDigits / swCustom4.ElapsedMilliseconds) / 1000.0 : 0;
        double v5MDps = (swCustom5.ElapsedMilliseconds > 0) ? (totalDigits / swCustom5.ElapsedMilliseconds) / 1000.0 : 0;
        
        // Output Times
        Console.WriteLine($"  - Standard BigInteger : {swStandard.ElapsedMilliseconds,5} ms | {stdMDps,6:F2} M-Digit/s (Baseline)");
        Console.WriteLine($"  - Custom Reduction V1 : {swCustom1.ElapsedMilliseconds,5} ms | {v1MDps,6:F2} M-Digit/s ({(double)swStandard.ElapsedMilliseconds / Math.Max(1, swCustom1.ElapsedMilliseconds):F2}x)");
        Console.WriteLine($"  - Ultra Optimized V2  : {swCustom2.ElapsedMilliseconds,5} ms | {v2MDps,6:F2} M-Digit/s ({(double)swStandard.ElapsedMilliseconds / Math.Max(1, swCustom2.ElapsedMilliseconds):F2}x vs Std)");
        Console.WriteLine($"  - Hybrid CPU V3 (Reduc): {hybridMsPerIter,6:F3} ms/iter (Extremely Fast Parallel Chunking)");
        Console.WriteLine($"  - FULL Hybrid CPU/GPU V4: {swCustom4.ElapsedMilliseconds,5} ms | {v4MDps,6:F2} M-Digit/s ({(double)swStandard.ElapsedMilliseconds / Math.Max(1, swCustom4.ElapsedMilliseconds):F2}x vs Std)");
        Console.WriteLine($"  - ZERO Overhead VRAM V5: {swCustom5.ElapsedMilliseconds,5} ms | {v5MDps,6:F2} M-Digit/s ({(double)swStandard.ElapsedMilliseconds / Math.Max(1, swCustom5.ElapsedMilliseconds):F2}x vs Std)");

        if (!pass1 || !pass2 || (E <= 10000 && !hybridPass) || !pass4 || !pass5)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [FAIL] Mismatch detected!");
            Console.WriteLine($"    Standard : {standardResult}");
            Console.WriteLine($"    Custom V1: {custom1Result} (Pass: {pass1})");
            Console.WriteLine($"    Custom V2: {custom2Result} (Pass: {pass2})");
            Console.WriteLine($"    Hybrid V3: Pass={hybridPass}");
            Console.WriteLine($"    Hybrid V4: {custom4Result} (Pass: {pass4})");
            Console.WriteLine($"    Zero V5  : {custom5Result} (Pass: {pass5})");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  [PASS] All logic matches standard result.");
        Console.ResetColor();
    }

    static bool TestHybridReduction(int E, BigInteger N)
    {
        // Generate a large number T = X^2 where X ~ 2^2E
        BigInteger T = BigInteger.Pow(BigInteger.One << (2 * E), 2) + 123456789;
        BigInteger expected = T % N;
        if (expected < 0) expected += N;

        int numWords = (2 * E + 1 + 31) / 32;
        byte[] tBytes = T.ToByteArray();
        uint[] data = new uint[numWords * 4]; // Give plenty of room for Q
        Buffer.BlockCopy(tBytes, 0, data, 0, tBytes.Length);

        HybridReduction.ReduceInPlace(data, E, numWords);

        // Extract result (which is now properly stored across the entire data array)
        uint highestWord = data[data.Length - 1];
        byte[] resBytes = new byte[data.Length * 4 + 1];
        Buffer.BlockCopy(data, 0, resBytes, 0, data.Length * 4);
        
        // Strictly check the most significant bit (MSB) of the very last word to determine the sign
        bool isNegative = (highestWord & 0x80000000) != 0;
        resBytes[data.Length * 4] = isNegative ? (byte)0xFF : (byte)0x00;
        
        BigInteger actual = new BigInteger(resBytes);
        
        // Final normalization loop (similar to what V2 does)
        // Since we did 2 passes, it is very close to [0, N-1], but might be slightly negative
        // or slightly above N.
        actual %= N;
        if (actual < 0) actual += N;

        if (expected == actual)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [PASS] HybridReduction output matches for E={E}");
            Console.ResetColor();
            return true;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [WARN] HybridReduction output MISMATCH for E={E}");
            Console.WriteLine($"         Expected: {expected}");
            Console.WriteLine($"         Actual  : {actual}");
            Console.ResetColor();
            return false;
        }
    }
}
