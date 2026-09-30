using System.Diagnostics;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Executes accelerated Fermat number Probable Prime (PRP) tests and NTT-based big integer squaring kernels on the GPU.
/// </summary>
public class FermatRunner : IDisposable
{
    /// <summary>Gets the Fermat exponent parameter.</summary>
    /// <remarks>
    /// Defined according to the Fermat number Probable Prime (PRP) test:
    /// <list type="bullet">
    ///   <item><description><see cref="E"/>: <c>F_n = 2^(2^E) + 1</c></description></item>
    /// </list>
    /// </remarks>
    public int E
    {
        get;
    }

    /// <summary>Gets the number of 32-bit words in the output buffer.</summary>
    public int OutNumWords { get; }

    private int _numWords;

    private GpuWorkspace _gpuWorkspace;

    // GpuUIntBigInteger buffers for Host-side orchestration
    private GpuUIntBigInteger _valueT;
    private GpuUIntBigInteger _valueTReduced;
    private GpuUIntBigInteger _mulResult;

    private GpuUIntBigInteger _quotient;
    private GpuUIntBigInteger _remainder;
    private GpuUIntBigInteger _remainderMask;
    private GpuUIntBigInteger _temp;

    private int _exponent;

    /// <summary>Initializes a new instance of the <see cref="FermatRunner"/> class for exponent <paramref name="e"/>.</summary>
    public FermatRunner(int e)
    {
        E = e;

        _numWords = (2 * E + 1 + 31) / 32;
        OutNumWords = _numWords * 2 + 16;
        _exponent = E;

        try
        {
            _gpuWorkspace = new GpuWorkspace(OutNumWords);
            _valueT = new GpuUIntBigInteger(_gpuWorkspace, OutNumWords);
            _valueTReduced = new GpuUIntBigInteger(_gpuWorkspace, _numWords);
            _mulResult = new GpuUIntBigInteger(_gpuWorkspace, _numWords * 2);

            _quotient = new GpuUIntBigInteger(_gpuWorkspace, OutNumWords);
            _remainder = new GpuUIntBigInteger(_gpuWorkspace, OutNumWords);
            _remainderMask = new GpuUIntBigInteger(_gpuWorkspace, OutNumWords);
            _temp = new GpuUIntBigInteger(_gpuWorkspace, OutNumWords);
        }
        catch
        {
            _gpuWorkspace?.Dispose();
            throw;
        }

        // Init ValueT = 2
        var initArray = new uint[OutNumWords];
        initArray[0] = 2;
        _gpuWorkspace.Provider.CopyHostToDevice(_valueT.DevicePointer, initArray, OutNumWords, _gpuWorkspace.Stream);

        // Init MaskR
        var bit2E1 = 2 * E + 1;
        var word2E1 = bit2E1 / 32;
        var rem2E1 = bit2E1 % 32;
        var maskArray = new uint[OutNumWords];
        for (var i = 0; i < word2E1; i++)
        {
            maskArray[i] = 0xFFFFFFFF;
        }

        if (rem2E1 > 0)
        {
            maskArray[word2E1] = (1U << rem2E1) - 1;
        }

        _gpuWorkspace.Provider.CopyHostToDevice(_remainderMask.DevicePointer, maskArray, OutNumWords, _gpuWorkspace.Stream);
    }

    /// <summary>Gets a copy of the current state buffer (ValueT).</summary>
    public uint[] GetState()
    {
        var copy = new uint[OutNumWords];
        _gpuWorkspace.Provider.CopyDeviceToHost(copy, _valueT.DevicePointer, OutNumWords, _gpuWorkspace.Stream);
        return copy;
    }

    /// <summary>Loads a previously saved state buffer (ValueT) into the context.</summary>
    public void LoadState(uint[] valueT)
    {
        if (valueT.Length != OutNumWords)
        {
            throw new ArgumentException($"Invalid state buffer size. Expected {OutNumWords}, got {valueT.Length}.");
        }

        _gpuWorkspace.Provider.CopyHostToDevice(_valueT.DevicePointer, valueT, OutNumWords, _gpuWorkspace.Stream);
    }

    private void MultiplySquareAndReduceHost()
    {
        // Copy the lower _numWords of _valueT into _valueTReduced
        _gpuWorkspace.Provider.CopyDeviceToDevice(_valueTReduced.DevicePointer, _valueT.DevicePointer, _numWords, _gpuWorkspace.Stream);

        // Square it using host-side NTT multiplication orchestration
        GpuBigIntMath.Mul(_valueTReduced, _valueTReduced, _mulResult);

        // Mega-Kernel reduction: replaces 11 separate kernel launches and H2D copies with a single pass
        // Executed in a single block (gridDimX = 1) with 256 threads to allow block-wide synchronization (__syncthreads).
        GpuBigIntFermatKernels.LaunchKernelFermatReduceMegaPtr(
            new GpuBigIntFermatKernels.AotContexts.FermatReduce
            {
                ValueT = _valueT.DevicePointer,
                MulResult = _mulResult.DevicePointer,
                Quotient = _quotient.DevicePointer,
                Remainder = _remainder.DevicePointer,
                Temp = _temp.DevicePointer,
                RemainderMask = _remainderMask.DevicePointer,
                NumWords = _numWords,
                OutNumWords = OutNumWords,
                TotalShiftBits = 2 * E + 1,
                Exponent = _exponent
            },
            gridDimX: 1, blockDimX: 256
        );

        // FIX: The Mega-Kernel writes the reduced output to _remainder, but it was never swapped back to _valueT.
        (_valueT, _remainder) = (_remainder, _valueT);
    }

    /// <summary>Executes the full Fermat Probable Prime (PRP) loop on the GPU and returns the final value.</summary>
    public (uint[] Result, long ElapsedMs) RunFermatPrp()
    {
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < E; i++)
        {
            MultiplySquareAndReduceHost();
        }

        GpuKernelLoader.SynchronizeDevice();
        sw.Stop();

        return (GetState(), sw.ElapsedMilliseconds);
    }

    /// <summary>Executes a specified number of squaring iterations on the GPU for benchmarking purposes.</summary>
    public void RunBenchmarkChunk(int chunkSize = 100)
    {
        for (var i = 0; i < chunkSize; i++)
        {
            MultiplySquareAndReduceHost();
        }

        GpuKernelLoader.SynchronizeDevice();
    }

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _gpuWorkspace.Dispose();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~FermatRunner()
    {
        Dispose(false);
    }
}
