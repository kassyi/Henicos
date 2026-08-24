using System.Diagnostics;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.GpuBigInt.Kernels;
using Kassyi.GSharp.GpuBigInt.Contexts;

namespace Kassyi.GSharp.GpuBigInt;

public class FermatRunner
{
    public int E { get; }
    private int _numWords;
    private int _outNumWords;
    private int _gM;

    private FermatContext _context;

    private int _maxGrid;
    private int _maxBlock;
    private int _sharedSize;

    public FermatRunner(int e)
    {
        E = e;
        Initialize();
    }

    private void Initialize()
    {
        _numWords = (2 * E + 1 + 31) / 32;
        _outNumWords = _numWords * 2 + 16;

        var requiredElements = (4 * E + 2 + 15) / 16;
        _gM = NextPowerOf2(requiredElements);

        _context = new FermatContext();

        // Dimensions
        _context.Dimensions.NumberOfWords = _numWords;
        _context.Dimensions.NumberOfWordsExtended = _outNumWords;
        _context.Dimensions.Exponent = E;
        _context.Dimensions.BitOffset2E1 = (2 * E + 1) % 32;

        // Buffers
        _context.Buffers.ValueT = new uint[_outNumWords];
        _context.Buffers.Quotient = new uint[_outNumWords];
        _context.Buffers.Remainder = new uint[_outNumWords];
        _context.Buffers.RemainderMask = new uint[_outNumWords];
        _context.Buffers.TemporaryBuffer = new uint[_outNumWords];

        var maxChunks = (_outNumWords + 255) / 256;
        _context.Buffers.CarryBuffer = new long[maxChunks + 1];

        // Init ValueT = 2
        _context.Buffers.ValueT[0] = 2;

        // Init MaskR
        var bit2E1 = 2 * E + 1;
        var word2E1 = bit2E1 / 32;
        var rem2E1 = bit2E1 % 32;
        for (var i = 0; i < word2E1; i++)
        {
            _context.Buffers.RemainderMask[i] = 0xFFFFFFFF;
        }

        if (rem2E1 > 0)
        {
            _context.Buffers.RemainderMask[word2E1] = (1U << rem2E1) - 1;
        }

        // Ntt Buffers
        _context.Ntt.TransformLength = _gM;
        _context.Ntt.Buffers.Buffer1 = new uint[_gM];
        _context.Ntt.Buffers.Buffer2 = new uint[_gM];
        _context.Ntt.Buffers.TemporaryBuffer = new uint[_gM];

        // Twiddle Factors
        _context.Ntt.TwiddleFactors.Forward1 = new uint[_gM];
        _context.Ntt.TwiddleFactors.Forward2 = new uint[_gM];
        _context.Ntt.TwiddleFactors.Inverse1 = new uint[_gM];
        _context.Ntt.TwiddleFactors.Inverse2 = new uint[_gM];

        ComputeTwiddleFactors();
        ComputeGridAndBlockSizes();
    }

    private int NextPowerOf2(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v++;
        return v;
    }

    private uint CpuModPow(ulong baseVal, ulong exp, uint p)
    {
        ulong res = 1;
        baseVal %= p;
        while (exp > 0)
        {
            if ((exp & 1) != 0)
            {
                res = (res * baseVal) % p;
            }

            baseVal = (baseVal * baseVal) % p;
            exp >>= 1;
        }

        return (uint)res;
    }

    private void ComputeTwiddleFactors()
    {
        var p1 = 4253024257;
        var p2 = 4246732801;
        var w1 = CpuModPow(5, (p1 - 1) / (uint)_gM, p1);
        var w2 = CpuModPow(2, (p2 - 1) / (uint)_gM, p2);

        var iw1 = CpuModPow(w1, p1 - 2, p1);
        var iw2 = CpuModPow(w2, p2 - 2, p2);

        ulong currentWeight1 = 1, currentInverseWeight1 = 1;
        ulong currentWeight2 = 1, currentInverseWeight2 = 1;

        for (var i = 0; i < _gM; i++)
        {
            _context.Ntt.TwiddleFactors.Forward1[i] = (uint)currentWeight1;
            _context.Ntt.TwiddleFactors.Inverse1[i] = (uint)currentInverseWeight1;
            _context.Ntt.TwiddleFactors.Forward2[i] = (uint)currentWeight2;
            _context.Ntt.TwiddleFactors.Inverse2[i] = (uint)currentInverseWeight2;

            currentWeight1 = (currentWeight1 * w1) % p1;
            currentInverseWeight1 = (currentInverseWeight1 * iw1) % p1;
            currentWeight2 = (currentWeight2 * w2) % p2;
            currentInverseWeight2 = (currentInverseWeight2 * iw2) % p2;
        }
    }

    private void ComputeGridAndBlockSizes()
    {
        _maxGrid = 1;
        _maxBlock = 256;

        if (_gM <= 512)
        {
            _maxBlock = _gM / 2;
            var pointGrid = (_gM + 255) / 256;
            _maxGrid = Math.Max(_maxGrid, pointGrid);
        }
        else
        {
            _maxBlock = 256;
            var dimensionN2 = 1;
            while (dimensionN2 * dimensionN2 < _gM)
            {
                dimensionN2 *= 2;
            }

            var dimensionN1 = _gM / dimensionN2;
            _maxGrid = Math.Max(_maxGrid, dimensionN1);
            _maxGrid = Math.Max(_maxGrid, dimensionN2);
            var pointGrid = (_gM + 255) / 256;
            _maxGrid = Math.Max(_maxGrid, pointGrid);
            var gridT1 = ((dimensionN2 + 31) / 32) * ((dimensionN1 + 31) / 32);
            var gridT2 = ((dimensionN1 + 31) / 32) * ((dimensionN2 + 31) / 32);
            _maxGrid = Math.Max(_maxGrid, gridT1);
            _maxGrid = Math.Max(_maxGrid, gridT2);
        }

        var numChunks = (_outNumWords + 255) / 256;
        _maxGrid = Math.Max(_maxGrid, numChunks);

        _sharedSize = 0;
        if (_gM <= 512)
        {
            _sharedSize = _gM * sizeof(uint);
        }
        else
        {
            var dimensionN2 = 1;
            while (dimensionN2 * dimensionN2 < _gM)
            {
                dimensionN2 *= 2;
            }

            var dimensionN1 = _gM / dimensionN2;
            var rowBuf = Math.Max(dimensionN1, dimensionN2) * sizeof(uint);
            var tileBuf = 32 * 33 * sizeof(uint);
            _sharedSize = Math.Max(rowBuf, tileBuf);
        }
    }

    public (uint[] Result, long EllapsedMs) RunFermatPrp()
    {
        var sw = Stopwatch.StartNew();

        // Dispatch to GSharp JIT
        GpuBigIntFermatKernelsWrapper.Launch_Kernel_StaticFermatLoop(
            _context,
            E.ToString(),
            (E - 4).ToString(),
            gridDimX: (uint)_maxGrid,
            blockDimX: (uint)_maxBlock,
            sharedMemBytes: (uint)_sharedSize
        );

        // Ensure GPU finishes execution before stopping the stopwatch and copying results
        JitEngine.SynchronizeDevice();

        sw.Stop();

        var resultData = new uint[_outNumWords];
        Array.Copy(_context.Buffers.ValueT, resultData, _outNumWords);

        return (resultData, sw.ElapsedMilliseconds);
    }

    public void RunBenchmarkChunk(int chunkSize = 100)
    {
        GpuBigIntBenchmarkKernelsWrapper.Launch_Kernel_BenchmarkSquareChunk(
            _context,
            chunkSize,
            gridDimX: (uint)_maxGrid,
            blockDimX: (uint)_maxBlock,
            sharedMemBytes: (uint)_sharedSize
        );

        // Ensure GPU finishes execution before returning to BenchmarkDotNet
        JitEngine.SynchronizeDevice();
    }
}

