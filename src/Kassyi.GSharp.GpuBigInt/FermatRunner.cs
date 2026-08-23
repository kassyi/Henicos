using System;
using System.Diagnostics;

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
        
        int requiredElements = (4 * E + 2 + 15) / 16;
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
        
        int maxChunks = (_outNumWords + 255) / 256;
        _context.Buffers.CarryBuffer = new long[maxChunks + 1];

        // Init ValueT = 2
        _context.Buffers.ValueT[0] = 2;

        // Init MaskR
        int bit2E1 = 2 * E + 1;
        int word2E1 = bit2E1 / 32;
        int rem2E1 = bit2E1 % 32;
        for (int i = 0; i < word2E1; i++)
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
            if ((exp & 1) != 0) res = (res * baseVal) % p;
            baseVal = (baseVal * baseVal) % p;
            exp >>= 1;
        }
        return (uint)res;
    }

    private void ComputeTwiddleFactors()
    {
        uint p1 = 4253024257;
        uint p2 = 4246732801;
        uint w1 = CpuModPow(5, (p1 - 1) / (uint)_gM, p1);
        uint w2 = CpuModPow(2, (p2 - 1) / (uint)_gM, p2);
        
        uint iw1 = CpuModPow(w1, p1 - 2, p1);
        uint iw2 = CpuModPow(w2, p2 - 2, p2);
        
        ulong curr_w1 = 1, curr_iw1 = 1;
        ulong curr_w2 = 1, curr_iw2 = 1;
        
        for (int i = 0; i < _gM; i++)
        {
            _context.Ntt.TwiddleFactors.Forward1[i] = (uint)curr_w1;
            _context.Ntt.TwiddleFactors.Inverse1[i] = (uint)curr_iw1;
            _context.Ntt.TwiddleFactors.Forward2[i] = (uint)curr_w2;
            _context.Ntt.TwiddleFactors.Inverse2[i] = (uint)curr_iw2;
            
            curr_w1 = (curr_w1 * w1) % p1;
            curr_iw1 = (curr_iw1 * iw1) % p1;
            curr_w2 = (curr_w2 * w2) % p2;
            curr_iw2 = (curr_iw2 * iw2) % p2;
        }
    }

    private void ComputeGridAndBlockSizes()
    {
        _maxGrid = 1;
        _maxBlock = 256;
        
        if (_gM <= 512)
        {
            _maxBlock = _gM / 2;
            int pointGrid = (_gM + 255) / 256;
            _maxGrid = Math.Max(_maxGrid, pointGrid);
        }
        else
        {
            _maxBlock = 256; 
            int N2 = 1;
            while (N2 * N2 < _gM) N2 *= 2;
            int N1 = _gM / N2;
            _maxGrid = Math.Max(_maxGrid, N1);
            _maxGrid = Math.Max(_maxGrid, N2);
            int pointGrid = (_gM + 255) / 256;
            _maxGrid = Math.Max(_maxGrid, pointGrid);
            int gridT1 = ((N2 + 31) / 32) * ((N1 + 31) / 32);
            int gridT2 = ((N1 + 31) / 32) * ((N2 + 31) / 32);
            _maxGrid = Math.Max(_maxGrid, gridT1);
            _maxGrid = Math.Max(_maxGrid, gridT2);
        }
        
        int numChunks = (_outNumWords + 255) / 256;
        _maxGrid = Math.Max(_maxGrid, numChunks);
        
        _sharedSize = 0;
        if (_gM <= 512)
        {
            _sharedSize = _gM * sizeof(uint);
        }
        else
        {
            int N2 = 1;
            while (N2 * N2 < _gM) N2 *= 2;
            int N1 = _gM / N2;
            int rowBuf = Math.Max(N1, N2) * sizeof(uint);
            int tileBuf = 32 * 33 * sizeof(uint);
            _sharedSize = Math.Max(rowBuf, tileBuf);
        }
    }

    public (uint[] Result, long EllapsedMs) RunFermatPrp()
    {
        // Warmup JIT Compilation
        GpuBigIntFermatKernelsWrapper.Launch_Kernel_StaticFermatLoop(
            _context,
            gridDimX: 1,
            blockDimX: 1,
            sharedMemBytes: 0
        );

        var sw = Stopwatch.StartNew();
        
        // Dispatch to GSharp JIT
        GpuBigIntFermatKernelsWrapper.Launch_Kernel_StaticFermatLoop(
            _context,
            gridDimX: (uint)_maxGrid,
            blockDimX: (uint)_maxBlock,
            sharedMemBytes: (uint)_sharedSize
        );
        
        sw.Stop();
        
        var resultData = new uint[_outNumWords];
        Array.Copy(_context.Buffers.ValueT, resultData, _outNumWords);
        
        return (resultData, sw.ElapsedMilliseconds);
    }
}
