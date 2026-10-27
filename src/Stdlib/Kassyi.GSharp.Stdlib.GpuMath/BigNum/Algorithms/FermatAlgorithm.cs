
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
    /// The largest exponent computed on the host: for <c>E &lt;= 31</c>, <c>N &lt; 2^63</c> and a square fits
    /// <see cref="UInt128"/>. The GPU fold needs <c>c = 2^E - 11 &gt; 0</c>, which these exponents also cover.
    /// </summary>
    private const int HostStageMaxExponent = 31;

    private readonly ResidueEngine _engine;
    private readonly bool _pinsFallbackMode;
    private readonly bool _cpuFallbackAtCreation;
    private bool _disposed;

    /// <summary>
    /// Gets the Fermat number exponent parameter $E$, where the target test number is $F_E = 2^{2^E} + 1$.
    /// </summary>
    public int E
    {
        get;
    }

    /// <summary>
    /// Gets the number of 32-bit words of a state as <see cref="GetState"/> returns it and <see cref="LoadState"/> takes
    /// it: <c>ceil((2E + 1) / 32)</c>.
    /// </summary>
    public int OutNumWords
    {
        get;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FermatAlgorithm"/> class configured for Fermat exponent <paramref name="e"/>.
    /// </summary>
    /// <param name="e">The exponent $E$, from 1 to 134,217,727.</param>
    /// <param name="base">The base $a$ the squaring chain starts from (at least 2).</param>
    /// <remarks>
    /// <para>
    /// For <c>E &lt;= 31</c> (<c>N &lt; 2^64</c>) the chain runs on the host and uses no GPU memory. Larger exponents
    /// square by NTT and reduce with the <c>QuadraticFoldReduceKernel</c> fold on the GPU, or, when the instance is created
    /// in CPU fallback mode, with <see cref="System.Numerics.BigInteger"/> and the host fold.
    /// </para>
    /// <para>
    /// For <c>E &gt;= 32</c>, the CPU fallback mode in effect at construction is kept for the instance's lifetime;
    /// calling a member after <see cref="GpuKernelLoader.ForceCpuFallback"/> changed it throws
    /// <see cref="InvalidOperationException"/>. The host stage (<c>E &lt;= 31</c>) uses neither mode's buffers and ignores
    /// the switch.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="e"/> is out of range, or <paramref name="base"/> is
    /// less than 2.</exception>
    /// <exception cref="InvalidOperationException">Thrown when GPU context initialization fails.</exception>
    public FermatAlgorithm(int e, uint @base = 2)
    {
        QuadraticFoldModulus.ThrowIfExponentOutOfRange(e);
        if (@base < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(@base), @base, "The base must be at least 2.");
        }

        E = e;
        _pinsFallbackMode = e > HostStageMaxExponent;
        _cpuFallbackAtCreation = _pinsFallbackMode && GpuKernelLoader.IsCpuFallbackMode;
        _engine = CreateEngine(e, @base, _cpuFallbackAtCreation);
        OutNumWords = _engine.Words;
    }

    private static ResidueEngine CreateEngine(int e, uint @base, bool isCpuFallback) =>
        e <= HostStageMaxExponent
            ? new HostResidueEngine(e, @base)
            : new DeviceResidueEngine(new QuadraticFoldModulus(e), @base, isCpuFallback);

    /// <summary>
    /// Copies the current state to host memory as its residue modulo <c>N</c>.
    /// </summary>
    /// <returns>The canonical residue <c>u mod N</c>, <see cref="OutNumWords"/> words.</returns>
    public uint[] GetState()
    {
        ThrowIfUnusable();
        return _engine.ReadCanonical(Register.State);
    }

    /// <summary>
    /// Asynchronously copies the current state to host memory as its residue modulo <c>N</c>, without blocking the
    /// calling thread while queued iterations complete.
    /// </summary>
    /// <param name="cancellationToken">A token that stops waiting; queued device work is not canceled.</param>
    /// <returns>The canonical residue <c>u mod N</c>, <see cref="OutNumWords"/> words.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public ValueTask<uint[]> GetStateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable();
        return _engine.ReadCanonicalAsync(Register.State, cancellationToken);
    }

    /// <summary>
    /// Replaces the current state with a residue previously returned by <see cref="GetState"/>.
    /// </summary>
    /// <param name="valueT">The residue, <see cref="OutNumWords"/> words, below <c>N</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="valueT"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="valueT"/> does not have <see cref="OutNumWords"/> words or is
    /// not below <c>N</c>.</exception>
    public void LoadState(uint[] valueT)
    {
        ThrowIfUnusable();
        ArgumentNullException.ThrowIfNull(valueT);

        if (valueT.Length != OutNumWords)
        {
            throw new ArgumentException($"Invalid state buffer size. Expected {OutNumWords}, got {valueT.Length}.",
                nameof(valueT));
        }

        if (!_engine.IsBelowModulus(valueT))
        {
            throw new ArgumentException("The state must be a residue below N = 2^(2E+1) - 2^E + 11.", nameof(valueT));
        }

        _engine.Write(Register.State, valueT);
    }

    /// <summary>
    /// Squares the state <c>E</c> times and returns the result and the elapsed time.
    /// </summary>
    /// <returns>A tuple containing the canonical state (see <see cref="GetState"/>) and the elapsed execution time in
    /// milliseconds.</returns>
    public (uint[] Result, long ElapsedMs) RunFermatPrp()
    {
        ThrowIfUnusable();

        var sw = Stopwatch.StartNew();

        _engine.SquareState(E);
        _engine.Synchronize();
        sw.Stop();

        return (_engine.ReadCanonical(Register.State), sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Executes the Fermat Probable Prime (PRP) loop with periodic Gerbicz-Li self-verification and automatic checkpoint rollback.
    /// </summary>
    /// <param name="blockSize">The number of squarings $B$ per Gerbicz accumulation step ($d \leftarrow d \cdot u \bmod N$).</param>
    /// <param name="checkStepBlocks">The number of $B$-sized blocks between full $u_0 \cdot d^{2^B} \equiv d_{\text{new}} \pmod N$ checks.</param>
    /// <returns>A tuple containing the canonical state, elapsed milliseconds, number of passed checks, and number of rollbacks.</returns>
    public (uint[] Result, long ElapsedMs, int ChecksPassed, int Rollbacks) RunFermatPrpWithGerbiczCheck(
        int blockSize = 100, int checkStepBlocks = 10)
    {
        ThrowIfUnusable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(checkStepBlocks);

        var sw = Stopwatch.StartNew();

        var one = new uint[OutNumWords];
        one[0] = 1u;
        _engine.Write(Register.CheckD, one);

        var verifiedK = 0;
        var verifiedU = _engine.ReadCanonical(Register.State);
        var verifiedD = one;

        var checkInterval = blockSize * checkStepBlocks;
        var skipNextCheckUpdate = false;
        var checksPassed = 0;
        var rollbacks = 0;

        var k = 0;
        while (k < E)
        {
            if (skipNextCheckUpdate)
            {
                skipNextCheckUpdate = false;
            }
            else if (k % blockSize == 0)
            {
                _engine.Multiply(Register.CheckD, Register.CheckD, Register.State);
            }

            var stepIters = Math.Min(blockSize - (k % blockSize), E - k);
            _engine.SquareState(stepIters);
            k += stepIters;

            var shouldCheck = (k % checkInterval == 0 && k % blockSize == 0) || (k == E && k % blockSize == 0);
            if (!shouldCheck)
            {
                continue;
            }

            // Gerbicz-Li check (gpuowl doCheck): verify d_old * u == u_0 * (d_old^(2^blockSize)) mod N
            _engine.Copy(Register.CheckAux, Register.CheckD);
            for (var s = 0; s < blockSize; s++)
            {
                _engine.Multiply(Register.CheckAux, Register.CheckAux, Register.CheckAux);
            }

            _engine.Multiply(Register.CheckAux, Register.CheckAux, Register.Base);
            _engine.Multiply(Register.CheckD, Register.CheckD, Register.State);

            var dHost = _engine.ReadCanonical(Register.CheckD);
            var auxHost = _engine.ReadCanonical(Register.CheckAux);

            if (dHost.AsSpan().SequenceEqual(auxHost))
            {
                checksPassed++;
                skipNextCheckUpdate = true;
                verifiedK = k;
                verifiedU = _engine.ReadCanonical(Register.State);
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
                _engine.Write(Register.State, verifiedU);
                _engine.Write(Register.CheckD, verifiedD);
                skipNextCheckUpdate = false;
            }
        }

        _engine.Synchronize();
        sw.Stop();

        return (_engine.ReadCanonical(Register.State), sw.ElapsedMilliseconds, checksPassed, rollbacks);
    }

    /// <summary>
    /// Squares the state <c>E</c> times like <see cref="RunFermatPrp"/>, but waits for the GPU without blocking the
    /// calling thread.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that stops waiting. An already canceled token prevents any work; a later cancellation leaves the queued
    /// iterations running, after which the state is only defined once the workspace stream has drained.
    /// </param>
    /// <returns>A tuple containing the canonical state and the elapsed execution time in milliseconds.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// Graph preparation (one warm-up iteration, capture and instantiation of a chunk length not seen before) still runs
    /// on the calling thread; only the wait for the iterations is asynchronous.
    /// </remarks>
    public async ValueTask<(uint[] Result, long ElapsedMs)> RunFermatPrpAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable();
        cancellationToken.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();

        _engine.SquareState(E);
        await _engine.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        sw.Stop();

        return (await _engine.ReadCanonicalAsync(Register.State, cancellationToken).ConfigureAwait(false),
            sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Squares the state <paramref name="chunkSize"/> times without checking, for throughput measurement.
    /// </summary>
    /// <param name="chunkSize">The number of squaring iterations to execute; zero or less does nothing.</param>
    public void RunBenchmarkChunk(int chunkSize = 100)
    {
        ThrowIfUnusable();

        _engine.SquareState(chunkSize);
        _engine.Synchronize();
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
        ThrowIfUnusable();

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        _engine.SquareState(chunkSize);
        return _engine.SynchronizeAsync(cancellationToken);
    }

    /// <summary>
    /// Releases unmanaged and optionally managed resources.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
    /// <exception cref="InvalidOperationException">For <c>E &gt;= 32</c>, the CPU fallback mode differs from the one at
    /// construction; nothing
    /// is released, so that host code never touches device memory (or the reverse). Restore the mode and dispose
    /// again.</exception>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            ThrowIfFallbackModeChanged();
            _engine.Dispose();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ThrowIfUnusable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFallbackModeChanged();
    }

    /// <summary>
    /// Throws when the CPU fallback mode differs from the one the instance was created in: device buffers would then go
    /// to host code, or host buffers to CUDA kernels, which corrupts the CUDA context (#647).
    /// </summary>
    private void ThrowIfFallbackModeChanged()
    {
        if (_pinsFallbackMode && GpuKernelLoader.IsCpuFallbackMode != _cpuFallbackAtCreation)
        {
            throw new InvalidOperationException(
                $"This FermatAlgorithm was created with the CPU fallback {(_cpuFallbackAtCreation ? "on" : "off")}, and it is now {(_cpuFallbackAtCreation ? "off" : "on")}. Switching the CPU fallback requires a new instance.");
        }
    }

    /// <summary>The residues a chain keeps: the state <c>u</c>, the Gerbicz-Li product <c>d</c> and check value, and the base.</summary>
    private enum Register
    {
        State,
        CheckD,
        CheckAux,
        Base
    }

    /// <summary>
    /// The residues modulo <c>N</c> of one chain and the operations on them. Each register holds a value below
    /// <c>2^(m+1)</c> congruent to its residue (redundant form); reads return the canonical residue.
    /// </summary>
    private abstract class ResidueEngine : IDisposable
    {
        protected const int RegisterCount = 4;

        /// <summary>Gets the word count of a canonical residue.</summary>
        public abstract int Words { get; }

        /// <summary>Returns whether <paramref name="words"/> is below <c>N</c>.</summary>
        public abstract bool IsBelowModulus(ReadOnlySpan<uint> words);

        /// <summary>Writes a residue below <c>N</c> to a register.</summary>
        public abstract void Write(Register register, ReadOnlySpan<uint> words);

        /// <summary>Reads the canonical residue of a register, after the queued work.</summary>
        public abstract uint[] ReadCanonical(Register register);

        /// <summary>Reads the canonical residue of a register without blocking while the queued work completes.</summary>
        public abstract ValueTask<uint[]> ReadCanonicalAsync(Register register, CancellationToken cancellationToken);

        /// <summary>Copies one register to another.</summary>
        public abstract void Copy(Register destination, Register source);

        /// <summary>Queues <paramref name="count"/> squarings of the state (none when it is zero or less).</summary>
        public abstract void SquareState(int count);

        /// <summary>Queues <c>target = left * right mod N</c>; <paramref name="target"/> may be an operand.</summary>
        public abstract void Multiply(Register target, Register left, Register right);

        /// <summary>Waits for the queued work.</summary>
        public abstract void Synchronize();

        /// <summary>Waits for the queued work without blocking the calling thread.</summary>
        public abstract ValueTask SynchronizeAsync(CancellationToken cancellationToken);

        /// <inheritdoc />
        public abstract void Dispose();
    }

    /// <summary>
    /// The host stage, <c>E &lt;= 31</c>: <c>N &lt; 2^63</c>, residues are canonical <see cref="ulong"/>s and a product
    /// is reduced with <see cref="UInt128"/>. No GPU memory is used.
    /// </summary>
    private sealed class HostResidueEngine : ResidueEngine
    {
        private readonly ulong _modulus;
        private readonly ulong[] _registers = new ulong[RegisterCount];

        public HostResidueEngine(int e, uint @base)
        {
            _modulus = (1UL << (2 * e + 1)) - (1UL << e) + 11;
            Words = (2 * e + 1 + 31) / 32;
            _registers[(int)Register.State] = @base % _modulus;
            _registers[(int)Register.Base] = @base % _modulus;
        }

        public override int Words { get; }

        public override bool IsBelowModulus(ReadOnlySpan<uint> words) => ToUInt64(words) < _modulus;

        public override void Write(Register register, ReadOnlySpan<uint> words) =>
            _registers[(int)register] = ToUInt64(words);

        public override uint[] ReadCanonical(Register register)
        {
            var value = _registers[(int)register];
            var words = new uint[Words];
            words[0] = (uint)value;
            if (Words > 1)
            {
                words[1] = (uint)(value >> 32);
            }

            return words;
        }

        public override ValueTask<uint[]> ReadCanonicalAsync(Register register, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled<uint[]>(cancellationToken)
                : ValueTask.FromResult(ReadCanonical(register));

        public override void Copy(Register destination, Register source) =>
            _registers[(int)destination] = _registers[(int)source];

        public override void SquareState(int count)
        {
            ref var state = ref _registers[(int)Register.State];
            for (var i = 0; i < count; i++)
            {
                state = MultiplyMod(state, state);
            }
        }

        public override void Multiply(Register target, Register left, Register right) =>
            _registers[(int)target] = MultiplyMod(_registers[(int)left], _registers[(int)right]);

        public override void Synchronize()
        {
            // Everything runs synchronously on the host.
        }

        public override ValueTask SynchronizeAsync(CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled(cancellationToken) : ValueTask.CompletedTask;

        public override void Dispose()
        {
            // No unmanaged resources.
        }

        // Both factors are below N < 2^63, so the product fits UInt128.
        private ulong MultiplyMod(ulong left, ulong right) => (ulong)((UInt128)left * right % _modulus);

        private static ulong ToUInt64(ReadOnlySpan<uint> words) =>
            words.Length > 1 ? words[0] | ((ulong)words[1] << 32) : words[0];
    }

    /// <summary>
    /// The device stage, <c>E &gt;= 32</c>: residues of <c>L</c> words in buffers this engine owns, squared by NTT with an
    /// owned scratch and reduced by <c>QuadraticFoldReduceKernel</c>, which writes back into the squared register; in CPU
    /// fallback mode, multiplied with <see cref="System.Numerics.BigInteger"/> and reduced by
    /// <see cref="QuadraticFoldModulus.FoldReference"/>.
    /// </summary>
    /// <remarks>
    /// On the GPU, squarings of the state run as CUDA Graphs, one per chain length, captured once (after one eager
    /// warm-up squaring for the life of the engine) and replayed until <see cref="Dispose"/>. Each squaring allocates
    /// nothing: the NTT scratch, the product and the fold temporaries are owned.
    /// </remarks>
    private sealed class DeviceResidueEngine : ResidueEngine
    {
        // Longest chain captured into one graph; longer requests replay it, the remainder has its own graph.
        private const int MaxGraphLength = 256;

        // QuadraticFoldReduceKernel runs as one block; the passes scan carries per warp, so a full block of 32 warps.
        private const uint FoldBlockSize = 1024;

        private readonly QuadraticFoldModulus _modulus;
        private readonly bool _isCpu;
        private readonly GpuWorkspace _workspace;
        private readonly GpuBigUInt[] _registers = new GpuBigUInt[RegisterCount];
        private readonly GpuBigUInt _product;
        private readonly GpuBigUInt _firstFold;
        private readonly GpuBigUInt _secondFold;
        private readonly KernelAdvancedMathHandler.NttSquareScratch? _squareScratch;
        private readonly Dictionary<int, (IntPtr Graph, IntPtr GraphExec)> _graphs = [];
        private bool _warmedUp;

        public DeviceResidueEngine(QuadraticFoldModulus modulus, uint @base, bool isCpu)
        {
            _modulus = modulus;
            _isCpu = isCpu;
            _workspace = new GpuWorkspace(modulus.ProductWords);
            try
            {
                for (var i = 0; i < RegisterCount; i++)
                {
                    _registers[i] = new GpuBigUInt(_workspace, modulus.Words);
                }

                _product = new GpuBigUInt(_workspace, modulus.ProductWords);
                if (!isCpu)
                {
                    // The fold temporaries are only used by the kernel; FoldReference rents its own on the host.
                    _firstFold = new GpuBigUInt(_workspace, modulus.FirstFoldWords);
                    _secondFold = new GpuBigUInt(_workspace, modulus.SecondFoldWords);
                    _squareScratch = KernelAdvancedMathHandler.NttSquareScratch.Allocate(_workspace,
                        GSharpKernelProvider.Instance.KernelAdvancedMath, modulus.Words);
                }

                // N > 2^64 > base, so the base is its own residue.
                var baseWords = new uint[modulus.Words];
                baseWords[0] = @base;
                Write(Register.State, baseWords);
                Write(Register.Base, baseWords);
            }
            catch
            {
                // The workspace releases every buffer allocated from it.
                _workspace.Dispose();
                throw;
            }
        }

        public override int Words => _modulus.Words;

        public override bool IsBelowModulus(ReadOnlySpan<uint> words)
        {
            var modulus = _modulus.Modulus;
            for (var i = words.Length - 1; i >= 0; i--)
            {
                if (words[i] != modulus[i])
                {
                    return words[i] < modulus[i];
                }
            }

            return false;
        }

        public override void Write(Register register, ReadOnlySpan<uint> words) =>
            _workspace.Provider.CopyHostToDevice(_registers[(int)register].DevicePointer, words.ToArray(),
                _modulus.Words, _workspace.Stream);

        public override uint[] ReadCanonical(Register register)
        {
            var words = new uint[_modulus.Words];
            _workspace.Provider.CopyDeviceToHost(words, _registers[(int)register].DevicePointer, _modulus.Words,
                _workspace.Stream);
            _modulus.Canonicalize(words);
            return words;
        }

        public override async ValueTask<uint[]> ReadCanonicalAsync(Register register,
            CancellationToken cancellationToken)
        {
            var words = new uint[_modulus.Words];
            await _workspace.Provider
                .CopyDeviceToHostAsync(words, _registers[(int)register].DevicePointer, _modulus.Words,
                    _workspace.Stream, cancellationToken)
                .ConfigureAwait(false);
            _modulus.Canonicalize(words);
            return words;
        }

        public override void Copy(Register destination, Register source) =>
            _workspace.Provider.CopyDeviceToDevice(_registers[(int)destination].DevicePointer,
                _registers[(int)source].DevicePointer, _modulus.Words, _workspace.Stream);

        public override void SquareState(int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (_isCpu)
            {
                // No graphs on the CPU fallback: every squaring runs directly (#647).
                for (var i = 0; i < count; i++)
                {
                    SquareOnce(Register.State);
                }

                return;
            }

            if (!_warmedUp)
            {
                // One eager squaring loads the kernels before the first capture, which may not load modules.
                SquareOnce(Register.State);
                _warmedUp = true;
                count--;
            }

            while (count > 0)
            {
                var length = Math.Min(count, MaxGraphLength);
                var repeats = count / length;
                var graphExec = GetOrCaptureGraph(length);
                for (var r = 0; r < repeats; r++)
                {
                    _workspace.Provider.LaunchGraph(graphExec, _workspace.Stream);
                }

                count -= length * repeats;
            }
        }

        public override void Multiply(Register target, Register left, Register right)
        {
            var leftBuffer = _registers[(int)left];
            var rightBuffer = _registers[(int)right];
            if (left == right && !_isCpu)
            {
                SquareOnce(left, target);
                return;
            }

            // Not in a squaring chain (Gerbicz-Li bookkeeping): the general product through the provider.
            _workspace.Provider.Mul(_workspace, leftBuffer, rightBuffer, _product);
            Fold(_registers[(int)target]);
        }

        public override void Synchronize() => _workspace.Provider.SynchronizeStream(_workspace.Stream);

        public override ValueTask SynchronizeAsync(CancellationToken cancellationToken) =>
            _workspace.Provider.SynchronizeStreamAsync(_workspace.Stream, cancellationToken);

        public override void Dispose()
        {
            foreach (var (graph, graphExec) in _graphs.Values)
            {
                _workspace.Provider.DestroyGraph(graph, graphExec);
            }

            _graphs.Clear();

            // Written out (not '?.') so that the ownership analyzer (GSH0022) sees the call.
            if (_squareScratch != null)
            {
                _squareScratch.Dispose();
            }

            _firstFold.Dispose();
            _secondFold.Dispose();
            _product.Dispose();
            foreach (var register in _registers)
            {
                register.Dispose();
            }

            _workspace.Dispose();
        }

        private void SquareOnce(Register register) => SquareOnce(register, register);

        /// <summary><c>target = source^2 mod N</c>, written back in place when they are the same register.</summary>
        private void SquareOnce(Register source, Register target)
        {
            var sourceBuffer = _registers[(int)source];
            if (_squareScratch is { } scratch)
            {
                GSharpKernelProvider.Instance.KernelAdvancedMath.Square(_workspace, sourceBuffer, _product, scratch);
            }
            else
            {
                _workspace.Provider.Mul(_workspace, sourceBuffer, sourceBuffer, _product);
            }

            Fold(_registers[(int)target]);
        }

        /// <summary>Reduces <see cref="_product"/> into <paramref name="residue"/>.</summary>
        private void Fold(GpuBigUInt residue)
        {
            if (_isCpu)
            {
                FoldOnHost(residue);
                return;
            }

            GpuBigIntFermatKernels.LaunchQuadraticFoldReduceKernelPtr(
                new GpuBigIntFermatKernels.AotContexts.QuadraticFold
                {
                    Product = _product.DevicePointer,
                    FirstFold = _firstFold.DevicePointer,
                    SecondFold = _secondFold.DevicePointer,
                    Residue = residue.DevicePointer,
                    ProductWords = _modulus.ProductWords,
                    FirstFoldWords = _modulus.FirstFoldWords,
                    SecondFoldWords = _modulus.SecondFoldWords,
                    ResidueWords = _modulus.Words,
                    ModulusBits = _modulus.ModulusBits,
                    Exponent = _modulus.E,
                    ConstantTerm = QuadraticFoldModulus.ConstantTerm
                },
                gridDimX: 1, blockDimX: FoldBlockSize, hStream: _workspace.Stream);
        }

        // CPU fallback memory is host memory (Marshal.AllocHGlobal), so the reference fold reads and writes it directly.
        private unsafe void FoldOnHost(GpuBigUInt residue)
        {
            var product = new ReadOnlySpan<uint>((void*)_product.DevicePointer, _modulus.ProductWords);
            var result = new Span<uint>((void*)residue.DevicePointer, _modulus.Words);
            _modulus.FoldReference(product, result);
        }

        /// <summary>Returns the graph that squares the state <paramref name="length"/> times, capturing it on first use.</summary>
        /// <exception cref="InvalidOperationException">The capture produced no graph.</exception>
        private IntPtr GetOrCaptureGraph(int length)
        {
            if (_graphs.TryGetValue(length, out var cached))
            {
                return cached.GraphExec;
            }

            var provider = _workspace.Provider;
            var stream = _workspace.Stream;
            provider.BeginCapture(stream);
            try
            {
                for (var i = 0; i < length; i++)
                {
                    SquareOnce(Register.State);
                }
            }
            catch (Exception captureError)
            {
                // End the capture so that the stream works again, and release the partial graph, then report the
                // original error (with the cleanup error, if the cleanup fails too).
                try
                {
                    provider.DestroyGraph(provider.EndCapture(stream), IntPtr.Zero);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(captureError, cleanupError);
                }

                throw;
            }

            var graph = provider.EndCapture(stream);

            if (graph == IntPtr.Zero)
            {
                // Returning here would silently run fewer squarings than requested (#647).
                throw new InvalidOperationException(
                    $"Capturing {length} squarings on the GPU workspace stream produced no CUDA Graph.");
            }

            var graphExec = IntPtr.Zero;
            try
            {
                graphExec = provider.InstantiateGraph(graph);
            }
            catch
            {
                provider.DestroyGraph(graph, graphExec);
                throw;
            }

            _graphs.Add(length, (graph, graphExec));
            return graphExec;
        }
    }
}
