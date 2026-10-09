
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Executes accelerated Fermat number Probable Prime (PRP) tests and NTT-based big integer squaring kernels on NVIDIA GPUs.
/// </summary>
/// <remarks>
/// <para>Performs iterative squaring and modular reduction modulo $F_E = 2^{2^E} + 1$ using GPU mega-kernels and host orchestration.</para>
/// <para><b>Memory Management:</b> Implements <see cref="IDisposable"/> to release all underlying GPU buffers and workspace allocations.</para>
/// </remarks>
public class FermatAlgorithm : IDisposable
{
    /// <summary>
    /// Gets the Fermat number exponent parameter $E$, where the target test number is $F_E = 2^{2^E} + 1$.
    /// </summary>
    public int E
    {
        get;
    }

    /// <summary>
    /// Gets the number of 32-bit words allocated in the output state buffer.
    /// </summary>
    public int OutNumWords
    {
        get;
    }

    private int _numWords;

    private GpuWorkspace _gpuWorkspace;

    // GpuBigUInt buffers for Host-side orchestration
    private GpuBigUInt _valueT;
    private GpuBigUInt _valueTReduced;
    private GpuBigUInt _mulResult;

    private GpuBigUInt _quotient;
    private GpuBigUInt _remainder;
    private GpuBigUInt _remainderMask;
    private GpuBigUInt _temp;

    private int _exponent;

    /// <summary>
    /// Initializes a new instance of the <see cref="FermatAlgorithm"/> class configured for Fermat exponent <paramref name="e"/>.
    /// </summary>
    /// <param name="e">The Fermat exponent $E$.</param>
    /// <exception cref="InvalidOperationException">Thrown when GPU context initialization fails.</exception>
    public FermatAlgorithm(int e)
    {
        E = e;

        _numWords = (2 * E + 1 + 31) / 32;
        OutNumWords = _numWords * 2 + 16;
        _exponent = E;

        try
        {
            _gpuWorkspace = new GpuWorkspace(OutNumWords);
            _valueT = new GpuBigUInt(_gpuWorkspace, OutNumWords);
            _valueTReduced = new GpuBigUInt(_gpuWorkspace, _numWords);
            _mulResult = new GpuBigUInt(_gpuWorkspace, _numWords * 2);

            _quotient = new GpuBigUInt(_gpuWorkspace, OutNumWords);
            _remainder = new GpuBigUInt(_gpuWorkspace, OutNumWords);
            _remainderMask = new GpuBigUInt(_gpuWorkspace, OutNumWords);
            _temp = new GpuBigUInt(_gpuWorkspace, OutNumWords);
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

        _gpuWorkspace.Provider.CopyHostToDevice(_remainderMask.DevicePointer, maskArray, OutNumWords,
            _gpuWorkspace.Stream);
    }

    /// <summary>
    /// Copies and returns the current state buffer (<c>ValueT</c>) from GPU device memory to host memory.
    /// </summary>
    /// <returns>An array of 32-bit words containing the current state.</returns>
    [ExcludeFromCodeCoverage]
    public uint[] GetState()
    {
        var copy = new uint[OutNumWords];
        _gpuWorkspace.Provider.CopyDeviceToHost(copy, _valueT.DevicePointer, OutNumWords, _gpuWorkspace.Stream);
        return copy;
    }

    /// <summary>
    /// Loads a previously saved state buffer (<c>ValueT</c>) into the GPU device execution context.
    /// </summary>
    /// <param name="valueT">An array containing the state buffer words to load.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="valueT"/> length does not equal <see cref="OutNumWords"/>.</exception>
    [ExcludeFromCodeCoverage]
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
        _gpuWorkspace.Provider.CopyDeviceToDevice(_valueTReduced.DevicePointer, _valueT.DevicePointer, _numWords,
            _gpuWorkspace.Stream);

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
            gridDimX: 1, blockDimX: 256, hStream: _gpuWorkspace.Stream
        );

        (_valueT, _remainder) = (_remainder, _valueT);
    }

    private void RunWithGraph(int iterations)
    {
        if (iterations <= 0)
        {
            return;
        }

        var stream = _gpuWorkspace.Stream;
        if (_gpuWorkspace.Provider is IGpuGraphProvider graphProvider)
        {
            // Warm-up to ensure allocations and static cache transfers complete outside capture
            MultiplySquareAndReduceHost();
            iterations--;

            if (iterations == 0)
            {
                return;
            }

            graphProvider.BeginCapture(stream);
            IntPtr graph;
            try
            {
                for (var i = 0; i < iterations; i++)
                {
                    MultiplySquareAndReduceHost();
                }
            }
            finally
            {
                graph = graphProvider.EndCapture(stream);
            }

            if (graph == IntPtr.Zero)
            {
                return;
            }

            var graphExec = graphProvider.InstantiateGraph(graph);

            try
            {
                graphProvider.LaunchGraph(graphExec, stream);
            }
            finally
            {
                graphProvider.DestroyGraph(graph, graphExec);
            }
        }
        else
        {
            for (var i = 0; i < iterations; i++)
            {
                MultiplySquareAndReduceHost();
            }
        }
    }

    /// <summary>
    /// Executes the full Fermat Probable Prime (PRP) loop on the GPU and returns the final result words and elapsed time.
    /// </summary>
    /// <returns>A tuple containing the final state array and the elapsed execution time in milliseconds.</returns>
    public (uint[] Result, long ElapsedMs) RunFermatPrp()
    {
        var sw = Stopwatch.StartNew();

        RunWithGraph(E);

        GpuKernelLoader.SynchronizeDevice();
        sw.Stop();

        return (GetState(), sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Executes a specified number of squaring iterations on the GPU for benchmarking purposes.
    /// </summary>
    /// <param name="chunkSize">The number of squaring iterations to execute.</param>
    public void RunBenchmarkChunk(int chunkSize = 100)
    {
        RunWithGraph(chunkSize);

        GpuKernelLoader.SynchronizeDevice();
    }

    private bool _disposed;

    /// <summary>
    /// Releases unmanaged and optionally managed resources.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _valueT.Dispose();
            _valueTReduced.Dispose();
            _mulResult.Dispose();
            _quotient.Dispose();
            _remainder.Dispose();
            _remainderMask.Dispose();
            _temp.Dispose();
            _gpuWorkspace.Dispose();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="FermatAlgorithm"/> class.
    /// </summary>
    ~FermatAlgorithm()
    {
        Dispose(false);
    }
}
