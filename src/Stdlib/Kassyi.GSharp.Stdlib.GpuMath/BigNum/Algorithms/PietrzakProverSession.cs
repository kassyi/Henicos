namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Owns a <see cref="PietrzakSchedule"/> and its <see cref="IPietrzakLeafStore"/> during a forward squaring chain,
/// recording leaf states and finalizing the dual-segment <see cref="FermatPrpProof"/> once the final Gerbicz-Li check passes.
/// </summary>
internal sealed class PietrzakProverSession : IChainLeafRecorder, IDisposable
{
    private readonly PietrzakSchedule _schedule;
    private readonly IPietrzakLeafStore _store;

    public PietrzakProverSession(FermatPrpProofHeader header, string? leafFilePath)
    {
        _schedule = new PietrzakSchedule(header);
        _store = CreateStore(_schedule, leafFilePath);
    }

    public long NextStopAfter(long iteration) => _schedule.NextLeafAfter(iteration);

    public void RecordIfLeaf(long iteration, ISquaringModulus modulus)
    {
        if (_schedule.TryGetLeafSlot(iteration, out var leafSlot))
        {
            _store.WriteSlot(leafSlot, modulus.ReadCanonical(SquaringRegister.State));
        }
    }

    public void PrepareRun(long startIteration)
    {
        if (startIteration <= 0)
        {
            _store.ResetLeaves();
            return;
        }

        for (var slot = 0; slot < _schedule.TotalLeafSlots; slot++)
        {
            var leafIteration = _schedule.GetLeafIteration(slot);
            if (leafIteration < startIteration && !_store.HasLeafSlot(slot))
            {
                throw new InvalidOperationException(
                    $"Cannot resume Pietrzak proof generation from iteration {startIteration}: leaf slot {slot} at iteration {leafIteration} was not recorded in the leaf store. Configure FermatPrpOptions.ProofLeafFile to persist leaves across restarts.");
            }
        }
    }

    public FermatPrpProof FinalizeProof(ISquaringModulus modulus, PietrzakClaim endpoints)
    {
        for (var slot = 0; slot < _schedule.TotalLeafSlots; slot++)
        {
            if (!_store.HasLeafSlot(slot))
            {
                throw new InvalidOperationException(
                    $"Pietrzak finalization is missing leaf slot {slot} (iteration {_schedule.GetLeafIteration(slot)}).");
            }
        }

        var math = new PietrzakArithmetic(modulus);
        var words = _schedule.ResidueWords;
        var alpha = _schedule.DeriveMergeChallenge(endpoints.X, endpoints.Y);
        var leafCount = _schedule.SegmentLeafCount;

        var leftBuffer = new uint[words];
        var rightBuffer = new uint[words];
        uint[] currentX = [];

        for (var j = 0; j < leafCount; j++)
        {
            _store.ReadSlot(j, leftBuffer);
            _store.ReadSlot(leafCount + j, rightBuffer);
            var merged = math.MulPow128(rightBuffer, alpha, leftBuffer);
            _store.WriteSlot(j, merged);
            if (j == 0)
            {
                currentX = merged;
            }
        }

        var claim = new PietrzakClaim(currentX, math.MulPow128(endpoints.Y, alpha, endpoints.X));
        var witnesses = new ReadOnlyMemory<uint>[_schedule.ProofPower];

        for (var round = 0; round < _schedule.ProofPower; round++)
        {
            var half = 1 << (_schedule.ProofPower - 1 - round);
            var witnessMu = new uint[words];
            _store.ReadSlot(half, witnessMu);
            witnesses[round] = witnessMu;

            if (round == _schedule.ProofPower - 1)
            {
                break;
            }

            var challenge = _schedule.DeriveRoundChallenge(round, claim, witnessMu);
            var nextY = math.MulPow128(witnessMu, challenge, claim.Y);

            for (var j = 0; j < half; j++)
            {
                _store.ReadSlot(j, leftBuffer);
                _store.ReadSlot(j + half, rightBuffer);
                var folded = math.MulPow128(leftBuffer, challenge, rightBuffer);
                _store.WriteSlot(j, folded);
                if (j == 0)
                {
                    currentX = folded;
                }
            }

            claim = new PietrzakClaim(currentX, nextY);
        }

        return new FermatPrpProof(_schedule.Header, endpoints.X, endpoints.Y, witnesses);
    }

    public void Flush() => _store.Flush();

    public void Dispose()
    {
        if (_store != null)
        {
            _store.Dispose();
        }
    }

    private static IPietrzakLeafStore CreateStore(PietrzakSchedule schedule, string? leafFilePath) =>
        leafFilePath != null
            ? new FilePietrzakLeafStore(leafFilePath, schedule)
            : new MemoryPietrzakLeafStore(schedule);
}
