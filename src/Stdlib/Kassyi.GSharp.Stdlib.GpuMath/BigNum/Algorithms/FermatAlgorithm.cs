
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
    /// Asynchronously copies the current state buffer (<c>ValueT</c>) to host memory, without blocking the calling
    /// thread while queued iterations complete.
    /// </summary>
    /// <param name="cancellationToken">A token that stops waiting; queued device work is not canceled.</param>
    /// <returns>An array of 32-bit words containing the current state.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async ValueTask<uint[]> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var copy = new uint[OutNumWords];
        await _gpuWorkspace.Provider
            .CopyDeviceToHostAsync(copy, _valueT.DevicePointer, OutNumWords, _gpuWorkspace.Stream, cancellationToken)
            .ConfigureAwait(false);
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

    /// <summary>
    /// Enqueues <paramref name="iterations"/> squaring iterations on the workspace stream without waiting for them.
    /// </summary>
    /// <remarks>
    /// The first iteration runs eagerly as a warm-up and the rest are captured into a CUDA Graph, which includes host
    /// synchronization. The graph is destroyed right after its launch; the driver defers the release until it completes.
    /// </remarks>
    private void EnqueueIterations(int iterations)
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

    private void MultiplyOperandsAndReduceHost(GpuBigUInt leftSource, GpuBigUInt rightSource, GpuBigUInt targetDest)
    {
        _gpuWorkspace.Provider.CopyDeviceToDevice(_valueTReduced.DevicePointer, leftSource.DevicePointer, _numWords,
            _gpuWorkspace.Stream);

        if (leftSource.DevicePointer == rightSource.DevicePointer)
        {
            GpuBigIntMath.Mul(_valueTReduced, _valueTReduced, _mulResult);
        }
        else
        {
            using var rightReduced = _gpuWorkspace.RentTemp(_numWords);
            _gpuWorkspace.Provider.CopyDeviceToDevice(rightReduced.DevicePointer, rightSource.DevicePointer, _numWords,
                _gpuWorkspace.Stream);
            GpuBigIntMath.Mul(_valueTReduced, rightReduced, _mulResult);
        }

        GpuBigIntFermatKernels.LaunchKernelFermatReduceMegaPtr(
            new GpuBigIntFermatKernels.AotContexts.FermatReduce
            {
                ValueT = targetDest.DevicePointer,
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

        _gpuWorkspace.Provider.CopyDeviceToDevice(targetDest.DevicePointer, _remainder.DevicePointer, OutNumWords,
            _gpuWorkspace.Stream);
    }

    private static BigInteger ExtractCanonicalModN(uint[] rawWords, int e)
    {
        var x = BigInteger.One << e;
        var n = (BigInteger.One << (2 * e + 1)) - x + 11;
        var highestWord = rawWords[^1];
        var resBytes = new byte[rawWords.Length * 4 + 1];
        Buffer.BlockCopy(rawWords, 0, resBytes, 0, rawWords.Length * 4);
        var isNegative = (highestWord & 0x80000000) != 0;
        resBytes[rawWords.Length * 4] = isNegative ? (byte)0xFF : (byte)0x00;

        var actual = new BigInteger(resBytes) % n;
        if (actual < 0)
        {
            actual += n;
        }

        return actual;
    }

    /// <summary>
    /// Executes the full Fermat Probable Prime (PRP) loop on the GPU and returns the final result words and elapsed time.
    /// </summary>
    /// <returns>A tuple containing the final state array and the elapsed execution time in milliseconds.</returns>
    public (uint[] Result, long ElapsedMs) RunFermatPrp()
    {
        var sw = Stopwatch.StartNew();

        EnqueueIterations(E);

        GpuKernelLoader.SynchronizeDevice();
        sw.Stop();

        return (GetState(), sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Executes the Fermat Probable Prime (PRP) loop with periodic Gerbicz-Li self-verification and automatic checkpoint rollback.
    /// </summary>
    /// <param name="blockSize">The number of squarings $B$ per Gerbicz accumulation step ($d \leftarrow d \cdot u \bmod N$).</param>
    /// <param name="checkStepBlocks">The number of $B$-sized blocks between full $u_0 \cdot d^{2^B} \equiv d_{\text{new}} \pmod N$ checks.</param>
    /// <returns>A tuple containing the final state words, elapsed milliseconds, number of passed checks, and number of rollbacks.</returns>
    public (uint[] Result, long ElapsedMs, int ChecksPassed, int Rollbacks) RunFermatPrpWithGerbiczCheck(
        int blockSize = 100, int checkStepBlocks = 10)
    {
        if (blockSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize));
        }

        if (checkStepBlocks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(checkStepBlocks));
        }

        var sw = Stopwatch.StartNew();
        using var checkD = new GpuBigUInt(_gpuWorkspace, OutNumWords);
        using var checkAux = new GpuBigUInt(_gpuWorkspace, OutNumWords);
        using var baseTwo = new GpuBigUInt(_gpuWorkspace, OutNumWords);

        var initOne = new uint[OutNumWords];
        initOne[0] = 1u;
        _gpuWorkspace.Provider.CopyHostToDevice(checkD.DevicePointer, initOne, OutNumWords, _gpuWorkspace.Stream);

        var initTwo = new uint[OutNumWords];
        initTwo[0] = 2u;
        _gpuWorkspace.Provider.CopyHostToDevice(baseTwo.DevicePointer, initTwo, OutNumWords, _gpuWorkspace.Stream);

        var verifiedK = 0;
        var verifiedU = GetState();
        var verifiedD = (uint[])initOne.Clone();

        var checkInterval = blockSize * checkStepBlocks;
        var skipNextCheckUpdate = false;
        var checksPassed = 0;
        var rollbacks = 0;
        var dRef = checkD;
        var auxRef = checkAux;

        var k = 0;
        while (k < E)
        {
            if (skipNextCheckUpdate)
            {
                skipNextCheckUpdate = false;
            }
            else if (k % blockSize == 0)
            {
                MultiplyOperandsAndReduceHost(dRef, _valueT, dRef);
            }

            var stepIters = Math.Min(blockSize - (k % blockSize), E - k);
            EnqueueIterations(stepIters);
            k += stepIters;

            var shouldCheck = (k % checkInterval == 0 && k % blockSize == 0) || (k == E && k % blockSize == 0);
            if (!shouldCheck)
            {
                continue;
            }

            // Gerbicz-Li check (gpuowl doCheck): verify d_old * u == u_0 * (d_old^(2^blockSize)) mod N
            _gpuWorkspace.Provider.CopyDeviceToDevice(auxRef.DevicePointer, dRef.DevicePointer, OutNumWords,
                _gpuWorkspace.Stream);
            for (var s = 0; s < blockSize; s++)
            {
                MultiplyOperandsAndReduceHost(auxRef, auxRef, auxRef);
            }

            MultiplyOperandsAndReduceHost(auxRef, baseTwo, auxRef);
            MultiplyOperandsAndReduceHost(dRef, _valueT, dRef);

            GpuKernelLoader.SynchronizeDevice();

            var dHost = new uint[OutNumWords];
            var auxHost = new uint[OutNumWords];
            _gpuWorkspace.Provider.CopyDeviceToHost(dHost, dRef.DevicePointer, OutNumWords, _gpuWorkspace.Stream);
            _gpuWorkspace.Provider.CopyDeviceToHost(auxHost, auxRef.DevicePointer, OutNumWords, _gpuWorkspace.Stream);

            if (ExtractCanonicalModN(dHost, E) == ExtractCanonicalModN(auxHost, E))
            {
                checksPassed++;
                skipNextCheckUpdate = true;
                verifiedK = k;
                verifiedU = GetState();
                verifiedD = dHost;
            }
            else
            {
                rollbacks++;
                if (rollbacks > 3)
                {
                    throw new InvalidOperationException("Gerbicz-Li PRP verification failed repeatedly.");
                }

                k = verifiedK;
                LoadState(verifiedU);
                _gpuWorkspace.Provider.CopyHostToDevice(dRef.DevicePointer, verifiedD, OutNumWords,
                    _gpuWorkspace.Stream);
                skipNextCheckUpdate = false;
            }
        }

        GpuKernelLoader.SynchronizeDevice();
        sw.Stop();

        return (GetState(), sw.ElapsedMilliseconds, checksPassed, rollbacks);
    }

    /// <summary>
    /// Executes the full Fermat Probable Prime (PRP) loop like <see cref="RunFermatPrp"/>, but waits for the GPU without
    /// blocking the calling thread.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that stops waiting. An already canceled token prevents any work; a later cancellation leaves the queued
    /// iterations running, after which the state is only defined once the workspace stream has drained.
    /// </param>
    /// <returns>A tuple containing the final state array and the elapsed execution time in milliseconds.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// Graph preparation (one warm-up iteration, capture and instantiation) still runs on the calling thread; only the
    /// wait for the iterations is asynchronous.
    /// </remarks>
    public async ValueTask<(uint[] Result, long ElapsedMs)> RunFermatPrpAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();

        EnqueueIterations(E);
        await _gpuWorkspace.Provider.SynchronizeStreamAsync(_gpuWorkspace.Stream, cancellationToken)
            .ConfigureAwait(false);
        sw.Stop();

        return (await GetStateAsync(cancellationToken).ConfigureAwait(false), sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Executes a specified number of squaring iterations on the GPU for benchmarking purposes.
    /// </summary>
    /// <param name="chunkSize">The number of squaring iterations to execute.</param>
    public void RunBenchmarkChunk(int chunkSize = 100)
    {
        EnqueueIterations(chunkSize);

        GpuKernelLoader.SynchronizeDevice();
    }

    /// <summary>
    /// Executes a specified number of squaring iterations like <see cref="RunBenchmarkChunk"/>, but waits for the GPU
    /// without blocking the calling thread.
    /// </summary>
    /// <param name="chunkSize">The number of squaring iterations to execute.</param>
    /// <param name="cancellationToken">
    /// A token that stops waiting. An already canceled token prevents any work; a later cancellation leaves the queued
    /// iterations running.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes when the iterations have finished.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public ValueTask RunBenchmarkChunkAsync(int chunkSize = 100, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        EnqueueIterations(chunkSize);
        return _gpuWorkspace.Provider.SynchronizeStreamAsync(_gpuWorkspace.Stream, cancellationToken);
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
