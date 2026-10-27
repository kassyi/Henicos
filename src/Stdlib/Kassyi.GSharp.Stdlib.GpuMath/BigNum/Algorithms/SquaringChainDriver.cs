namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// A point of a squaring chain that passed a Gerbicz-Li check: the chain can resume from it.
/// </summary>
/// <param name="Iteration">The number of squarings <c>k</c>, a multiple of the block size.</param>
/// <param name="State">The canonical <c>u_k</c>.</param>
/// <param name="Check">The canonical Gerbicz-Li product <c>d</c>, which includes <c>u_k</c>.</param>
/// <param name="Snapshot">The canonical snapshot value when <c>k</c> is past the snapshot point; otherwise empty.</param>
/// <param name="Final">The canonical value at the end of the chain (<c>u_K</c>) when <c>k</c> is past <c>K</c>; otherwise
/// empty. A point at <c>K' &gt; K</c> must carry it: the run resumed from there squares nothing and reads it.</param>
/// <param name="ChecksPassed">The checks passed up to this point.</param>
/// <param name="Rollbacks">The rollbacks up to this point.</param>
internal sealed record ChainPoint(
    long Iteration,
    uint[] State,
    uint[] Check,
    uint[] Snapshot,
    uint[] Final,
    int ChecksPassed,
    int Rollbacks);

/// <summary>The outcome of <see cref="SquaringChainDriver.RunAsync"/>.</summary>
/// <param name="EndPoint">The last verified point, at the end of the chain.</param>
/// <param name="Iterations">The squarings of the state actually run, including those repeated after rollbacks.</param>
internal readonly record struct ChainOutcome(ChainPoint EndPoint, long Iterations);

/// <summary>
/// Runs a squaring chain <c>u_(k+1) = u_k^2</c> on an <see cref="ISquaringModulus"/> under the Gerbicz-Li check
/// (gpuowl <c>Gpu::isPrimePRP</c>), saving the value at a snapshot point and at the end of the chain.
/// </summary>
/// <remarks>
/// <para>
/// With the block size <c>B</c>, at every block boundary <c>k</c> (a multiple of <c>B</c>) the product
/// <c>d = u_0 * u_B * ... * u_k</c> is extended by <c>u_k</c>. A check at <c>k</c> compares
/// <c>d_old^(2^B) * u_0</c> (<c>d_old</c> before the extension) with <c>d_old * u_k</c>: they are equal when every
/// squaring of the block was right. Checks run every <c>checkIntervalBlocks</c> blocks and at the end of the chain,
/// which is rounded up to a block boundary (<c>K' = roundUp(K, B)</c>, as gpuowl does), so every value read out of the
/// chain is followed by a check.
/// </para>
/// <para>
/// A failed check rolls back to the last verified point; its <c>d</c> already includes its <c>u_k</c>, so the chain
/// continues with the next block without extending <c>d</c> again. After <c>maxConsecutiveFailures</c> failures in a row
/// the run throws <see cref="InvalidOperationException"/>. Copies saved on the host (the verified point, the snapshot)
/// are outside the check, as in gpuowl.
/// </para>
/// </remarks>
internal sealed class SquaringChainDriver
{
    private readonly ISquaringModulus _modulus;

    /// <summary>Initializes a driver for chains of <paramref name="chainLength"/> squarings.</summary>
    /// <param name="modulus">The registers and operations.</param>
    /// <param name="chainLength">The number of squarings <c>K</c> whose result is saved in <see cref="SquaringRegister.Final"/>.</param>
    /// <param name="snapshotAt">The squaring count whose value is saved in <see cref="SquaringRegister.Snapshot"/> (at most <c>K</c>).</param>
    /// <param name="blockSize">The block size <c>B</c> (at least 1).</param>
    /// <param name="checkIntervalBlocks">The blocks between checks (at least 1).</param>
    /// <param name="maxConsecutiveFailures">The failed checks in a row that end the run (at least 1).</param>
    public SquaringChainDriver(ISquaringModulus modulus, long chainLength, long snapshotAt, int blockSize,
        int checkIntervalBlocks, int maxConsecutiveFailures)
    {
        _modulus = modulus;
        ChainLength = chainLength;
        SnapshotAt = snapshotAt;
        BlockSize = blockSize;
        CheckInterval = (long)checkIntervalBlocks * blockSize;
        MaxConsecutiveFailures = maxConsecutiveFailures;
        EndIteration = (chainLength + blockSize - 1) / blockSize * blockSize;
    }

    /// <summary>Gets <c>K</c>.</summary>
    public long ChainLength { get; }

    /// <summary>Gets the snapshot point.</summary>
    public long SnapshotAt { get; }

    /// <summary>Gets <c>B</c>.</summary>
    public int BlockSize { get; }

    /// <summary>Gets the squarings between checks.</summary>
    public long CheckInterval { get; }

    /// <summary>Gets the failed checks in a row that end the run.</summary>
    public int MaxConsecutiveFailures { get; }

    /// <summary>Gets <c>K' = roundUp(K, B)</c>, where the chain ends with its last check.</summary>
    public long EndIteration { get; }

    /// <summary>
    /// Gets or sets a test hook that receives the position after every squaring of the state and, when it returns
    /// <see langword="true"/>, corrupts the state there (one bit of the low word). <see langword="null"/> in production;
    /// when set, the state is squared one at a time so that every position is reached.
    /// </summary>
    /// <remarks>Shows that the Gerbicz-Li check detects an error and that the rollback recovers from it.</remarks>
    internal Func<long, bool>? FaultInjection { get; set; }

    /// <summary>Returns the starting point <c>k = 0</c>: <c>u_0</c> and <c>d = u_0</c>.</summary>
    public static ChainPoint StartPoint(uint[] start) => new(0, start, start, [], [], 0, 0);

    /// <summary>
    /// Writes <paramref name="point"/> into the registers: the state, <c>d</c>, the snapshot when the point is at or past
    /// the snapshot point, and <c>u_K</c> when it is at or past the end of the chain.
    /// </summary>
    public void Load(ChainPoint point)
    {
        _modulus.Write(SquaringRegister.State, point.State);
        _modulus.Write(SquaringRegister.CheckD, point.Check);
        if (point.Iteration > SnapshotAt)
        {
            _modulus.Write(SquaringRegister.Snapshot, point.Snapshot);
        }
        else if (point.Iteration == SnapshotAt)
        {
            _modulus.Copy(SquaringRegister.Snapshot, SquaringRegister.State);
        }

        if (point.Iteration > ChainLength)
        {
            _modulus.Write(SquaringRegister.Final, point.Final);
        }
        else if (point.Iteration == ChainLength)
        {
            _modulus.Copy(SquaringRegister.Final, SquaringRegister.State);
        }
    }

    /// <summary>
    /// Runs the chain from <paramref name="start"/> to <see cref="EndIteration"/>, with
    /// <see cref="SquaringRegister.Start"/> holding <c>u_0</c>.
    /// </summary>
    /// <param name="start">A verified point (or <see cref="StartPoint"/>) to resume from.</param>
    /// <param name="synchronous">Whether to wait synchronously (the returned task is then complete).</param>
    /// <param name="onVerified">Called with every point that passes a check, including the last one.</param>
    /// <param name="onBlock">Called at every block boundary with the iteration, the checks passed and the rollbacks.</param>
    /// <param name="cancellationToken">Checked at every block boundary. On cancellation the registers are set back to the
    /// last verified point, which <paramref name="onVerified"/> has received.</param>
    /// <returns>The outcome; <see cref="SquaringRegister.Snapshot"/> and <see cref="SquaringRegister.Final"/> hold their values.</returns>
    /// <exception cref="InvalidOperationException">The check failed <see cref="MaxConsecutiveFailures"/> times in a row.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async ValueTask<ChainOutcome> RunAsync(ChainPoint start, bool synchronous, Action<ChainPoint>? onVerified,
        Action<long, int, int>? onBlock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Load(start);

        var verified = start;
        var k = start.Iteration;
        var checksPassed = start.ChecksPassed;
        var rollbacks = start.Rollbacks;
        var consecutiveFailures = 0;
        long iterations = 0;

        while (k < EndIteration)
        {
            // One block: square up to the next boundary, stopping at the snapshot point and at the end of the chain.
            var boundary = (k / BlockSize + 1) * BlockSize;
            while (k < boundary)
            {
                var stop = boundary;
                if (k < SnapshotAt)
                {
                    stop = Math.Min(stop, SnapshotAt);
                }

                if (k < ChainLength)
                {
                    stop = Math.Min(stop, ChainLength);
                }

                var faultInjection = FaultInjection;
                if (faultInjection != null)
                {
                    stop = Math.Min(stop, k + 1);
                }

                _modulus.SquareState((int)(stop - k));
                iterations += stop - k;
                k = stop;
                if (k == SnapshotAt)
                {
                    _modulus.Copy(SquaringRegister.Snapshot, SquaringRegister.State);
                }

                if (k == ChainLength)
                {
                    _modulus.Copy(SquaringRegister.Final, SquaringRegister.State);
                }

                if (faultInjection != null && faultInjection(k))
                {
                    CorruptState();
                }
            }

            if (k % CheckInterval != 0 && k != EndIteration)
            {
                // d <- d * u_k
                _modulus.Multiply(SquaringRegister.CheckD, SquaringRegister.CheckD, SquaringRegister.State);
            }
            else
            {
                // aux = d_old^(2^B) * u_0, then d <- d_old * u_k; equal when the block was squared correctly.
                _modulus.Copy(SquaringRegister.CheckAux, SquaringRegister.CheckD);
                _modulus.Square(SquaringRegister.CheckAux, BlockSize);
                _modulus.Multiply(SquaringRegister.CheckAux, SquaringRegister.CheckAux, SquaringRegister.Start);
                _modulus.Multiply(SquaringRegister.CheckD, SquaringRegister.CheckD, SquaringRegister.State);

                var d = await ReadAsync(SquaringRegister.CheckD, synchronous).ConfigureAwait(false);
                var aux = await ReadAsync(SquaringRegister.CheckAux, synchronous).ConfigureAwait(false);
                if (d.AsSpan().SequenceEqual(aux))
                {
                    checksPassed++;
                    consecutiveFailures = 0;
                    var state = await ReadAsync(SquaringRegister.State, synchronous).ConfigureAwait(false);
                    var snapshot = k > SnapshotAt
                        ? await ReadAsync(SquaringRegister.Snapshot, synchronous).ConfigureAwait(false)
                        : [];
                    var final = k > ChainLength
                        ? await ReadAsync(SquaringRegister.Final, synchronous).ConfigureAwait(false)
                        : [];
                    verified = new ChainPoint(k, state, d, snapshot, final, checksPassed, rollbacks);
                    onVerified?.Invoke(verified);
                }
                else
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        throw new InvalidOperationException(
                            $"The Gerbicz-Li check failed {consecutiveFailures} times in a row (at iteration {k}, block size {BlockSize}); the last verified point is iteration {verified.Iteration}.");
                    }

                    // Back to the last verified point; its d already includes its u_k.
                    rollbacks++;
                    verified = verified with { Rollbacks = rollbacks };
                    Load(verified);
                    k = verified.Iteration;
                }
            }

            onBlock?.Invoke(k, checksPassed, rollbacks);
            if (cancellationToken.IsCancellationRequested)
            {
                await WaitAsync(synchronous).ConfigureAwait(false);
                Load(verified);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        await WaitAsync(synchronous).ConfigureAwait(false);
        return new ChainOutcome(verified, iterations);
    }

    /// <summary>Flips one low bit of the state (bit 0, or bit 1 when bit 0 would make it <c>N</c>).</summary>
    private void CorruptState()
    {
        var words = _modulus.ReadCanonical(SquaringRegister.State);
        words[0] ^= 1u;
        if (!_modulus.IsBelowModulus(words))
        {
            words[0] ^= 3u;
        }

        _modulus.Write(SquaringRegister.State, words);
    }

    private ValueTask<uint[]> ReadAsync(SquaringRegister register, bool synchronous) =>
        synchronous
            ? ValueTask.FromResult(_modulus.ReadCanonical(register))
            : _modulus.ReadCanonicalAsync(register, CancellationToken.None);

    private ValueTask WaitAsync(bool synchronous)
    {
        if (!synchronous)
        {
            return _modulus.SynchronizeAsync(CancellationToken.None);
        }

        _modulus.Synchronize();
        return ValueTask.CompletedTask;
    }
}
