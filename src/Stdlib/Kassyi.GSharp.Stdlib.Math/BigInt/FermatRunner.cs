using System;
using System.Diagnostics;
using Kassyi.GSharp.Runtime.Jit;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Executes accelerated Fermat number Probable Prime (PRP) tests and NTT-based big integer squaring kernels on the GPU.
/// </summary>
/// <remarks><para>Implements: <b>GEN-51</b></para></remarks>
public class FermatRunner : IDisposable
{
    /// <summary>Gets the Fermat exponent parameter <c>E</c> (testing <c>F_n = 2^(2^E) + 1</c>).</summary>
    public int E { get; }

    /// <summary>Gets the number of 32-bit words in the output buffer.</summary>
    public int OutNumWords => _outNumWords;

    private int _numWords;
    private int _outNumWords;

    private GpuContext _gpuContext;

    // GpuBigInteger buffers for Host-side orchestration
    private GpuBigInteger _valueT;
    private GpuBigInteger _valueTReduced;
    private GpuBigInteger _mulResult;
    
    private GpuBigInteger _quotient;
    private GpuBigInteger _remainder;
    private GpuBigInteger _remainderMask;
    private GpuBigInteger _temp;

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
        _valueT = new GpuBigInteger(_gpuContext, _outNumWords);
        _valueTReduced = new GpuBigInteger(_gpuContext, _numWords);
        _mulResult = new GpuBigInteger(_gpuContext, _numWords * 2);

        _quotient = new GpuBigInteger(_gpuContext, _outNumWords);
        _remainder = new GpuBigInteger(_gpuContext, _outNumWords);
        _remainderMask = new GpuBigInteger(_gpuContext, _outNumWords);
        _temp = new GpuBigInteger(_gpuContext, _outNumWords);

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

        // Copy _mulResult into the zero-padded _valueT (which is _outNumWords long)
        // First zero out _valueT
        var zeroMem = new uint[_outNumWords]; 
        _gpuContext.Provider.CopyHostToDevice(_valueT.DevicePointer, zeroMem, _outNumWords);
        _gpuContext.Provider.CopyDeviceToDevice(_valueT.DevicePointer, _mulResult.DevicePointer, _numWords * 2);

        // Reduce
        GpuBigIntMath.ShiftRight(_valueT, _bitOffset2E1, _quotient);
        GpuBigIntMath.BitwiseAnd(_valueT, _remainderMask, _remainder);
        
        GpuBigIntMath.ShiftLeft(_quotient, _exponent, _temp);
        GpuBigIntMath.Add(_remainder, _temp, _remainder);
        
        GpuBigIntMath.ShiftLeft(_quotient, 1, _temp);
        GpuBigIntMath.Sub(_remainder, _quotient, _remainder);
        GpuBigIntMath.Sub(_remainder, _temp, _remainder);
        
        GpuBigIntMath.ShiftLeft(_quotient, 3, _temp);
        GpuBigIntMath.Sub(_remainder, _temp, _valueT);
    }

    /// <summary>Executes the full Fermat Probable Prime (PRP) loop on the GPU and returns the final value.</summary>
    public (uint[] Result, long EllapsedMs) RunFermatPrp()
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < E; i++)
        {
            MultiplySquareAndReduceHost();
        }

        JitEngine.SynchronizeDevice();
        sw.Stop();

        return (GetState(), sw.ElapsedMilliseconds);
    }

    /// <summary>Executes a specified number of squaring iterations on the GPU for benchmarking purposes.</summary>
    public void RunBenchmarkChunk(int chunkSize = 100)
    {
        for (int i = 0; i < chunkSize; i++)
        {
            MultiplySquareAndReduceHost();
        }
        JitEngine.SynchronizeDevice();
    }

    public void Dispose()
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
}
