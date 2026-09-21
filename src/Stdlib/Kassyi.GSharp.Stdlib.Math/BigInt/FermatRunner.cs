using System.Diagnostics;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Executes accelerated Fermat number Probable Prime (PRP) tests and NTT-based big integer squaring kernels on the GPU.
/// </summary>
/// <remarks><para>Implements: <b>GEN-51</b></para></remarks>
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
    public int OutNumWords => _outNumWords;

    private int _numWords;
    private int _outNumWords;

    private GpuContext _gpuContext;

    // GpuUIntBigInteger buffers for Host-side orchestration
    private GpuUIntBigInteger _valueT;
    private GpuUIntBigInteger _valueTReduced;
    private GpuUIntBigInteger _mulResult;

    private GpuUIntBigInteger _quotient;
    private GpuUIntBigInteger _remainder;
    private GpuUIntBigInteger _remainderMask;
    private GpuUIntBigInteger _temp;

    private int _bitOffset2E1;
    private int _exponent;

    /// <summary>Initializes a new instance of the <see cref="FermatRunner"/> class for exponent <paramref name="e"/>.</summary>
    public FermatRunner(int e)
    {
        E = e;

        _numWords = (2 * E + 1 + 31) / 32;
        _outNumWords = _numWords * 2 + 16;
        _bitOffset2E1 = (2 * E + 1) % 32;
        _exponent = E;

        _gpuContext = new GpuContext(_outNumWords);
        _valueT = new GpuUIntBigInteger(_gpuContext, _outNumWords);
        _valueTReduced = new GpuUIntBigInteger(_gpuContext, _numWords);
        _mulResult = new GpuUIntBigInteger(_gpuContext, _numWords * 2);

        _quotient = new GpuUIntBigInteger(_gpuContext, _outNumWords);
        _remainder = new GpuUIntBigInteger(_gpuContext, _outNumWords);
        _remainderMask = new GpuUIntBigInteger(_gpuContext, _outNumWords);
        _temp = new GpuUIntBigInteger(_gpuContext, _outNumWords);

        // Init ValueT = 2
        var initArray = new uint[_outNumWords];
        initArray[0] = 2;
        _gpuContext.Provider.CopyHostToDevice(_valueT.DevicePointer, initArray, _outNumWords);

        // Init MaskR
        var bit2E1 = 2 * E + 1;
        var word2E1 = bit2E1 / 32;
        var rem2E1 = bit2E1 % 32;
        var maskArray = new uint[_outNumWords];
        for (var i = 0; i < word2E1; i++)
        {
            maskArray[i] = 0xFFFFFFFF;
        }

        if (rem2E1 > 0)
        {
            maskArray[word2E1] = (1U << rem2E1) - 1;
        }

        _gpuContext.Provider.CopyHostToDevice(_remainderMask.DevicePointer, maskArray, _outNumWords);
    }

    /// <summary>Gets a copy of the current state buffer (ValueT).</summary>
    public uint[] GetState()
    {
        var copy = new uint[_outNumWords];
        _gpuContext.Provider.CopyDeviceToHost(copy, _valueT.DevicePointer, _outNumWords);
        return copy;
    }

    /// <summary>Loads a previously saved state buffer (ValueT) into the context.</summary>
    public void LoadState(uint[] valueT)
    {
        if (valueT.Length != _outNumWords)
        {
            throw new ArgumentException($"Invalid state buffer size. Expected {_outNumWords}, got {valueT.Length}.");
        }

        _gpuContext.Provider.CopyHostToDevice(_valueT.DevicePointer, valueT, _outNumWords);
    }

    private void MultiplySquareAndReduceHost()
    {
        // Copy the lower _numWords of _valueT into _valueTReduced
        _gpuContext.Provider.CopyDeviceToDevice(_valueTReduced.DevicePointer, _valueT.DevicePointer, _numWords);

        // Square it using host-side NTT multiplication orchestration
        GpuBigIntMath.Mul(_valueTReduced, _valueTReduced, _mulResult);

        // Mega-Kernel reduction: replaces 11 separate kernel launches and H2D copies with a single pass
        GpuBigIntFermatKernelsWrapper.LaunchKernel_FermatReduce_Mega_Device(
            _valueT.DevicePointer, _mulResult.DevicePointer, _quotient.DevicePointer, _remainder.DevicePointer,
            _temp.DevicePointer, _remainderMask.DevicePointer,
            _numWords, _outNumWords, 2 * E + 1, _exponent,
            gridDimX: 1, blockDimX: 1
        );
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

    protected virtual void Dispose(bool disposing)
    {
        _valueT.Dispose();
        _valueTReduced.Dispose();
        _mulResult.Dispose();
        _quotient.Dispose();
        _remainder.Dispose();
        _remainderMask.Dispose();
        _temp.Dispose();
        _gpuContext.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
