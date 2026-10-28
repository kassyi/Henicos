
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Fermat probable-prime (PRP) test of <c>N(E) = 2^(2E+1) - 2^E + 11 = 2X^2 - X + 11</c> (<c>X = 2^E</c>), the number
/// G# was started for (<c>E = 71428571</c>, about 43 million digits).
/// </summary>
/// <remarks>
/// <para>
/// <b>Verdict.</b> <see cref="Run"/> decides whether <c>a^(N-1) = 1 (mod N)</c> for the base <c>a</c> (default 3). If not,
/// <c>N</c> is certainly composite; if so, <c>N</c> is a probable prime to base <c>a</c>. This is not a proof: most of
/// <c>N - 1</c> cannot be factored, so Pocklington or Proth certificates do not apply. When <c>gcd(a, N) != 1</c> the
/// result reports that divisor without any squaring (for <c>a = 3</c>, every even <c>E</c>, since then <c>3 | N</c>).
/// </para>
/// <para>
/// <b>Chain.</b> From <c>N - 1 = 2^(2E+1) - 2^E + 10</c>: <c>a^(N-1) = 1</c> if and only if
/// <c>u_K * a^10 = u_E (mod N)</c>, with <c>u_0 = a</c>, <c>u_(k+1) = u_k^2</c> and <c>K = 2E + 1</c>. The test is a chain
/// of <c>2E + 1</c> squarings and nothing else, as in gpuowl and PRST.
/// </para>
/// <para>
/// <b>Error check.</b> The whole chain runs under the Gerbicz-Li check (block size <c>B</c>, see
/// <see cref="FermatPrpOptions"/>); the chain is extended to the next block boundary so that a check follows every value
/// read out of it. A failed check rolls back to the last verified point; failing
/// <see cref="FermatPrpOptions.MaxConsecutiveRollbacks"/> times in a row throws. Copies kept outside the chain (the saved
/// <c>u_E</c>, a <see cref="FermatPrpCheckpoint"/> in a file) are not covered by the check, as in gpuowl.
/// </para>
/// <para>
/// <b>Stages.</b> <c>E &lt;= 31</c> (<c>N &lt; 2^64</c>) runs on the host with <see cref="UInt128"/>. Larger exponents keep
/// the residues on the GPU: NTT squaring and a dedicated fold (<c>GpuBigIntFermatKernels.QuadraticFoldReduceKernel</c>,
/// three passes of <c>r + q * (2^E - 11)</c>), replayed as CUDA Graphs. An instance created in CPU fallback mode
/// multiplies with <see cref="BigInteger"/> and folds on the host, with the same results. The largest exponent is
/// <see cref="MaxExponent"/>, set by the NTT length limit.
/// </para>
/// <para>
/// For <c>E &gt;= 32</c>, the CPU fallback mode in effect at construction is kept for the instance's lifetime; calling a
/// member after <see cref="GpuKernelLoader.ForceCpuFallback"/> changed it throws <see cref="InvalidOperationException"/>.
/// The host stage uses neither mode's buffers and ignores the switch.
/// </para>
/// </remarks>
public sealed class FermatAlgorithm : IDisposable
{
    /// <summary>
    /// The largest exponent, <c>2^27 - 1</c>: the square of a residue then fills the longest product the NTT
    /// multiplication supports (<c>2^24</c> words, transform length <c>2^25</c>).
    /// </summary>
    public const int MaxExponent = QuadraticFoldModulus.MaxExponent;

    private readonly int _blockSize;
    private readonly int _checkIntervalBlocks;
    private readonly long _endIteration;
    private ISquaringModulus? _modulus;
    private SquaringChainDriver? _driver;
    private readonly FermatPrpOptions _options;
    private readonly uint? _smallDivisor;
    private readonly uint[] _startWords = [];
    private readonly bool _pinsFallbackMode;
    private readonly bool _cpuFallbackAtCreation;
    private ChainPoint _lastVerified;
    private ChainPoint? _pendingRestore;
    private long _iteration;
    private bool _disposed;

    /// <summary>Initializes a test of <c>N(E)</c> to base <paramref name="base"/>.</summary>
    /// <param name="e">The exponent <c>E</c>, from 1 to <see cref="MaxExponent"/>.</param>
    /// <param name="base">The base <c>a</c> (default 3, as in gpuowl and PRST).</param>
    /// <param name="options">The Gerbicz-Li options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="e"/> is out of range; <paramref name="base"/> is less
    /// than 2 or is 0, 1 or <c>N - 1</c> modulo <c>N</c> (possible only for small <c>E</c>); an option is negative,
    /// <see cref="FermatPrpOptions.BlockSize"/> exceeds <c>2E + 1</c>, or
    /// <see cref="FermatPrpOptions.MaxConsecutiveRollbacks"/> is less than 1. Checked before any allocation.</exception>
    /// <exception cref="InvalidOperationException">GPU context initialization fails. When <c>gcd(a, N) != 1</c> nothing is
    /// allocated here, and the failure surfaces on the first <see cref="GetState"/>, <see cref="RunBenchmarkChunk"/> or
    /// <see cref="Restore"/> instead.</exception>
    public FermatAlgorithm(int e, uint @base = 3, FermatPrpOptions? options = null)
    {
        QuadraticFoldModulus.ThrowIfExponentOutOfRange(e);
        if (@base < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(@base), @base, "The base must be at least 2.");
        }

        _options = options ?? new FermatPrpOptions();
        ThrowIfInvalid(_options, 2L * e + 1);

        E = e;
        Base = @base;
        ModulusBits = 2 * e + 1;
        ChainLength = 2L * e + 1;
        if (e <= QuadraticHostModulus.MaxExponent)
        {
            ThrowIfTrivialBase(e, @base);
        }

        _smallDivisor = SmallDivisor(e, @base);
        _pinsFallbackMode = e > QuadraticHostModulus.MaxExponent;
        _cpuFallbackAtCreation = _pinsFallbackMode && GpuKernelLoader.IsCpuFallbackMode;
        ResidueWords = QuadraticFoldModulus.WordsOf(e);
        _blockSize = _options.BlockSize != 0
            ? _options.BlockSize
            : (int)Math.Clamp((long)Math.Sqrt(ChainLength), 2, 1000);
        _checkIntervalBlocks = _options.CheckIntervalBlocks != 0 ? _options.CheckIntervalBlocks : _blockSize;
        _endIteration = SquaringChainDriver.EndIterationOf(ChainLength, _blockSize);
        _startWords = StartWords(e, @base, ResidueWords);
        _lastVerified = SquaringChainDriver.StartPoint(_startWords);

        // A small divisor decides the test without any squaring: the registers, the NTT scratch and the fold buffers
        // (millions of words each for large E) are created only if a member that needs the state is called.
        if (_smallDivisor is null)
        {
            EnsureChain();
        }
    }

    /// <summary>Gets the exponent <c>E</c> of <c>N = 2^(2E+1) - 2^E + 11</c>.</summary>
    public int E { get; }

    /// <summary>Gets the base <c>a</c>.</summary>
    public uint Base { get; }

    /// <summary>Gets <c>2E + 1</c>, the bit position of the leading term of <c>N</c>.</summary>
    public int ModulusBits { get; }

    /// <summary>Gets the word count of a residue: <c>ceil((2E + 1) / 32)</c>.</summary>
    public int ResidueWords { get; }

    /// <summary>Gets the number of squarings of the test, <c>2E + 1</c>.</summary>
    public long ChainLength { get; }

    /// <summary>Gets the Gerbicz-Li block size in use (the option, or the automatic choice).</summary>
    public int BlockSize => _blockSize;

    /// <summary>
    /// Gets the squarings done on the state: the position of the last <see cref="Run"/> (or of a
    /// <see cref="Restore"/>) plus the squarings of <see cref="RunBenchmarkChunk"/> since.
    /// </summary>
    public long Iteration => _iteration;

    /// <summary>
    /// Gets or sets a test hook (internal): it receives the position after every squaring of the chain and, when it
    /// returns <see langword="true"/>, corrupts the state there. It shows that the Gerbicz-Li check detects errors;
    /// <see langword="null"/> (the default) costs two null checks at each stop of the chain (a block boundary, the
    /// snapshot point, the end of the chain), not one per squaring.
    /// </summary>
    internal Func<long, bool>? CorruptStateAt
    {
        get => _driver?.FaultInjection;
        set
        {
            EnsureChain();
            _driver.FaultInjection = value;
        }
    }

    /// <summary>Gets whether the residues and their buffers exist (for tests: a small divisor creates none).</summary>
    internal bool HasChain => _modulus != null;

    /// <summary>Returns <c>N = 2^(2E+1) - 2^E + 11</c>.</summary>
    /// <returns>The modulus; this allocates <c>O(E)</c> bits.</returns>
    public BigInteger GetModulus()
    {
        ThrowIfUnusable();
        return (BigInteger.One << ModulusBits) - (BigInteger.One << E) + QuadraticFoldModulus.ConstantTerm;
    }

    /// <summary>Runs the probable-prime test.</summary>
    /// <returns>The verdict, the two 64-bit residues and the work done.</returns>
    /// <remarks>
    /// The chain starts again from <c>u_0 = a</c>, unless <see cref="Restore"/> was called since the last run; then it
    /// resumes from that checkpoint. The state left behind is <c>u_K'</c> (<c>K'</c> is <see cref="ChainLength"/> rounded
    /// up to a block).
    /// </remarks>
    /// <exception cref="InvalidOperationException">The Gerbicz-Li check failed
    /// <see cref="FermatPrpOptions.MaxConsecutiveRollbacks"/> times in a row.</exception>
    public FermatPrpResult Run()
    {
        ThrowIfUnusable();

        // Synchronous: every await inside completes before it returns.
        return RunCoreAsync(synchronous: true, progress: null, CancellationToken.None).AsTask().GetAwaiter()
            .GetResult();
    }

    /// <summary>Runs the probable-prime test like <see cref="Run"/>, waiting for the GPU without blocking the thread.</summary>
    /// <param name="progress">Receives the position at every block boundary.</param>
    /// <param name="cancellationToken">
    /// Checked at every block boundary. An already canceled token does nothing. On cancellation the state goes back to
    /// the last point that passed a check; <see cref="CreateCheckpoint"/> returns it, and <see cref="Restore"/> with it
    /// lets a later run continue from there.
    /// </param>
    /// <returns>The verdict, the two 64-bit residues and the work done.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">The Gerbicz-Li check failed
    /// <see cref="FermatPrpOptions.MaxConsecutiveRollbacks"/> times in a row.</exception>
    public ValueTask<FermatPrpResult> RunAsync(IProgress<FermatPrpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable();
        return RunCoreAsync(synchronous: false, progress, cancellationToken);
    }

    /// <summary>Returns the last point of the chain that passed a Gerbicz-Li check.</summary>
    /// <returns>
    /// The checkpoint (the start of the chain before any check passed). It reflects <see cref="Run"/>,
    /// <see cref="RunAsync"/> and <see cref="Restore"/>, not <see cref="RunBenchmarkChunk"/>.
    /// </returns>
    public FermatPrpCheckpoint CreateCheckpoint()
    {
        ThrowIfUnusable();
        return ToCheckpoint(_lastVerified);
    }

    /// <summary>Sets the state to a checkpoint, from which the next <see cref="Run"/> continues.</summary>
    /// <param name="checkpoint">A checkpoint of a test with the same <c>E</c>, base and block size.</param>
    /// <remarks>
    /// The state and <see cref="Iteration"/> change at once: <see cref="GetState"/> returns the checkpoint's state and
    /// <see cref="CreateCheckpoint"/> returns this checkpoint until the next run.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="checkpoint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The checkpoint is of another test (<c>E</c>, base, block size), its position is not
    /// a block boundary of this chain, a residue has the wrong length or is not below <c>N</c>, or a count is
    /// negative.</exception>
    public void Restore(FermatPrpCheckpoint checkpoint)
    {
        ThrowIfUnusable();
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.E != E || checkpoint.Base != Base || checkpoint.BlockSize != BlockSize)
        {
            throw new ArgumentException(
                $"The checkpoint is of E = {checkpoint.E}, base {checkpoint.Base}, block size {checkpoint.BlockSize}; this test is E = {E}, base {Base}, block size {BlockSize}.",
                nameof(checkpoint));
        }

        if (checkpoint.Iteration < 0 || checkpoint.Iteration > _endIteration ||
            checkpoint.Iteration % BlockSize != 0)
        {
            throw new ArgumentException(
                $"The checkpoint position {checkpoint.Iteration} is not a block boundary of this chain (0 to {_endIteration}, step {BlockSize}).",
                nameof(checkpoint));
        }

        if (checkpoint.ChecksPassed < 0 || checkpoint.Rollbacks < 0)
        {
            throw new ArgumentException(
                $"The checkpoint counts {checkpoint.ChecksPassed} checks and {checkpoint.Rollbacks} rollbacks; neither can be negative.",
                nameof(checkpoint));
        }

        EnsureChain();
        var snapshotWords = checkpoint.Iteration > E ? ResidueWords : 0;
        var finalWords = checkpoint.Iteration > ChainLength ? ResidueWords : 0;
        ThrowIfNotResidue(checkpoint.State.Span, ResidueWords, "State");
        ThrowIfNotResidue(checkpoint.Check.Span, ResidueWords, "Check");
        ThrowIfNotResidue(checkpoint.SnapshotE.Span, snapshotWords, "SnapshotE");
        ThrowIfNotResidue(checkpoint.FinalK.Span, finalWords, "FinalK");

        var point = new ChainPoint(checkpoint.Iteration, checkpoint.State.ToArray(), checkpoint.Check.ToArray(),
            checkpoint.SnapshotE.ToArray(), checkpoint.FinalK.ToArray(), checkpoint.ChecksPassed,
            checkpoint.Rollbacks);
        _driver.Load(point);
        _lastVerified = point;
        _pendingRestore = point;
        _iteration = point.Iteration;
    }

    /// <summary>
    /// Squares the state <paramref name="iterations"/> times without any check, for throughput measurement.
    /// </summary>
    /// <param name="iterations">The number of squarings.</param>
    /// <remarks>
    /// This advances <see cref="Iteration"/> and changes the state, but it is not part of a test: the next
    /// <see cref="Run"/> starts its chain again (or from a <see cref="Restore"/>).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="iterations"/> is negative.</exception>
    public void RunBenchmarkChunk(int iterations = 100)
    {
        ThrowIfUnusable();
        ArgumentOutOfRangeException.ThrowIfNegative(iterations);

        EnsureChain();
        _modulus.SquareState(iterations);
        _modulus.Synchronize();
        _iteration += iterations;
    }

    /// <summary>
    /// Squares the state like <see cref="RunBenchmarkChunk"/>, but waits for the GPU without blocking the calling thread.
    /// </summary>
    /// <param name="iterations">The number of squarings.</param>
    /// <param name="cancellationToken">
    /// A token that stops waiting. An already canceled token prevents any work; a later cancellation leaves the queued
    /// squarings running.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes when the squarings have finished.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="iterations"/> is negative.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public ValueTask RunBenchmarkChunkAsync(int iterations = 100, CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable();
        ArgumentOutOfRangeException.ThrowIfNegative(iterations);

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        EnsureChain();
        _modulus.SquareState(iterations);
        _iteration += iterations;
        return _modulus.SynchronizeAsync(cancellationToken);
    }

    /// <summary>Copies the current state to host memory as its residue modulo <c>N</c>.</summary>
    /// <returns>The canonical <c>u mod N</c>, <see cref="ResidueWords"/> words.</returns>
    public uint[] GetState()
    {
        ThrowIfUnusable();
        EnsureChain();
        return _modulus.ReadCanonical(SquaringRegister.State);
    }

    /// <summary>
    /// Copies the current state to host memory like <see cref="GetState"/>, without blocking the calling thread while
    /// queued squarings complete.
    /// </summary>
    /// <param name="cancellationToken">A token that stops waiting; queued device work is not canceled.</param>
    /// <returns>The canonical <c>u mod N</c>, <see cref="ResidueWords"/> words.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public ValueTask<uint[]> GetStateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable();
        EnsureChain();
        return _modulus.ReadCanonicalAsync(SquaringRegister.State, cancellationToken);
    }

    /// <summary>Releases the residues, the scratch buffers, the CUDA Graphs and the workspace.</summary>
    /// <exception cref="InvalidOperationException">For <c>E &gt;= 32</c>, the CPU fallback mode differs from the one at
    /// construction; nothing is released, so that host code never touches device memory (or the reverse). Restore the
    /// mode and dispose again.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ThrowIfFallbackModeChanged();
        try
        {
            // Written out (not '?.') so that the ownership analyzer (GSH0022) sees the call.
            if (_modulus != null)
            {
                _modulus.Dispose();
            }
        }
        finally
        {
            // A failed release (an AggregateException from the CUDA Graphs) still leaves the instance disposed, so a
            // second Dispose does not release the buffers again.
            _disposed = true;
        }
    }

    private async ValueTask<FermatPrpResult> RunCoreAsync(bool synchronous, IProgress<FermatPrpProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        if (_smallDivisor is { } divisor)
        {
            return new FermatPrpResult(false, divisor, 0, 0, 0, 0, 0, stopwatch.Elapsed);
        }

        EnsureChain();
        var start = _pendingRestore ?? SquaringChainDriver.StartPoint(_startWords);
        if (_pendingRestore is null)
        {
            // A run that does not resume starts again from u_0: until its first check passes, u_0 is its last verified
            // point, so a cancellation reports the same position in Iteration, CreateCheckpoint and GetState.
            _lastVerified = start;
        }

        _pendingRestore = null;

        ChainOutcome outcome;
        try
        {
            outcome = await _driver.RunAsync(start, synchronous, OnVerified, (k, checks, rollbacks) =>
            {
                _iteration = k;
                progress?.Report(new FermatPrpProgress(k, _endIteration, checks, rollbacks));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The driver set the registers back to the last verified point.
            _iteration = _lastVerified.Iteration;
            throw;
        }

        // u_K * a^10 against u_E.
        // Into its own register: Final keeps u_K, so a run resumed from the last point (or run again) reads it again.
        _modulus.Multiply(SquaringRegister.Verdict, SquaringRegister.Final, SquaringRegister.Constant);
        var residue = _modulus.ReadCanonical(SquaringRegister.Verdict);
        var check = _modulus.ReadCanonical(SquaringRegister.Snapshot);
        _iteration = _endIteration;

        return new FermatPrpResult(
            residue.AsSpan().SequenceEqual(check),
            null,
            Low64(residue),
            Low64(check),
            outcome.Iterations,
            outcome.EndPoint.ChecksPassed,
            outcome.EndPoint.Rollbacks,
            stopwatch.Elapsed);
    }

    private void OnVerified(ChainPoint point)
    {
        _lastVerified = point;
        _options.OnVerifiedCheckpoint?.Invoke(ToCheckpoint(point));
    }

    private FermatPrpCheckpoint ToCheckpoint(ChainPoint point) =>
        new(E, Base, BlockSize, point.Iteration, point.State, point.Check, point.Snapshot, point.Final,
            point.ChecksPassed, point.Rollbacks);

    private void ThrowIfNotResidue(ReadOnlySpan<uint> words, int expectedWords, string name)
    {
        if (words.Length != expectedWords)
        {
            throw new ArgumentException($"The checkpoint's {name} has {words.Length} words; expected {expectedWords}.",
                "checkpoint");
        }

        EnsureChain();
        if (expectedWords > 0 && !_modulus.IsBelowModulus(words))
        {
            throw new ArgumentException($"The checkpoint's {name} is not below N.", "checkpoint");
        }
    }

    /// <summary>
    /// Creates the residues and their buffers when they do not exist yet: the modulus (host or device stage, in the
    /// CPU fallback mode fixed at construction), the start <c>u_0 = a</c>, the constant <c>a^10</c> and the chain driver.
    /// </summary>
    [MemberNotNull(nameof(_modulus), nameof(_driver))]
    private void EnsureChain()
    {
        if (_modulus != null && _driver != null)
        {
            return;
        }

        var modulus = CreateModulus(E, _cpuFallbackAtCreation);
        try
        {
            modulus.Write(SquaringRegister.Start, _startWords);
            modulus.Write(SquaringRegister.State, _startWords);
            modulus.Write(SquaringRegister.Constant,
                ToWords(BigInteger.ModPow(Base, 10, modulus.GetModulus()), ResidueWords));
            _driver = new SquaringChainDriver(modulus, ChainLength, E, _blockSize, _checkIntervalBlocks,
                _options.MaxConsecutiveRollbacks);
            _modulus = modulus;
        }
        catch
        {
            modulus.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Returns <c>u_0 = a mod N</c> in <paramref name="words"/> words without building <c>N</c>: for <c>E &gt;= 32</c>,
    /// <c>N &gt; 2^64 &gt; a</c>.
    /// </summary>
    private static uint[] StartWords(int e, uint @base, int words)
    {
        var start = new uint[words];
        ulong value = @base;
        if (e <= QuadraticHostModulus.MaxExponent)
        {
            value %= (1UL << (2 * e + 1)) - (1UL << e) + QuadraticFoldModulus.ConstantTerm;
        }

        start[0] = (uint)value;
        if (words > 1)
        {
            start[1] = (uint)(value >> 32);
        }

        return start;
    }

    private static ISquaringModulus CreateModulus(int e, bool isCpuFallback) =>
        e <= QuadraticHostModulus.MaxExponent
            ? new QuadraticHostModulus(e)
            : new QuadraticDeviceModulus(new QuadraticFoldModulus(e), isCpuFallback);

    private static void ThrowIfInvalid(FermatPrpOptions options, long chainLength)
    {
        if (options.BlockSize < 0 || options.BlockSize > chainLength)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.BlockSize,
                $"BlockSize must be from 0 (automatic) to the chain length 2E + 1 = {chainLength}.");
        }

        if (options.CheckIntervalBlocks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.CheckIntervalBlocks,
                "CheckIntervalBlocks must not be negative.");
        }

        if (options.MaxConsecutiveRollbacks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxConsecutiveRollbacks,
                "MaxConsecutiveRollbacks must be at least 1.");
        }
    }

    /// <summary>Rejects a base that is 0, 1 or <c>N - 1</c> modulo <c>N</c>, which only a small <c>N</c> allows.</summary>
    private static void ThrowIfTrivialBase(int e, uint @base)
    {
        var n = (1UL << (2 * e + 1)) - (1UL << e) + QuadraticFoldModulus.ConstantTerm;
        var reduced = @base % n;
        if (reduced == 0 || reduced == 1 || reduced == n - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(@base), @base,
                $"The base is {reduced} modulo N = {n}; a base of 0, 1 or N - 1 says nothing about N.");
        }
    }

    /// <summary>Returns <c>gcd(a, N)</c> when it is not 1, from <c>N mod a = 2x^2 - x + 11 mod a</c> with <c>x = 2^E mod a</c>.</summary>
    private static uint? SmallDivisor(int e, uint @base)
    {
        ulong x = 1;
        ulong power = 2 % @base;
        for (var bits = e; bits > 0; bits >>= 1)
        {
            if ((bits & 1) != 0)
            {
                x = x * power % @base;
            }

            power = power * power % @base;
        }

        // 2x^2 + 11 + (a - x) is non-negative because x < a.
        var nModBase = (ulong)(((UInt128)2 * x * x + QuadraticFoldModulus.ConstantTerm + (@base - x)) % @base);
        var gcd = (ulong)BigInteger.GreatestCommonDivisor(@base, nModBase);
        return gcd == 1 ? null : (uint)gcd;
    }

    private static uint[] ToWords(BigInteger value, int words)
    {
        var result = new uint[words];
        value.TryWriteBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(result.AsSpan()), out _,
            isUnsigned: true);
        return result;
    }

    private static ulong Low64(ReadOnlySpan<uint> words) =>
        words.Length > 1 ? words[0] | ((ulong)words[1] << 32) : words[0];

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
}
