#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// Property, soundness and fault-injection tests of dual-segment batched Pietrzak proofs in
/// <see cref="FermatAlgorithm"/> (<see cref="FermatPrpProof"/> and <see cref="FermatAlgorithm.VerifyProof"/>).
/// </summary>
/// <remarks>
/// <para>
/// Properties checked:
/// <list type="bullet">
/// <item><description><b>P-completeness</b>: honest runs across host (<c>E &lt;= 31</c>), GPU (<c>E &gt;= 32</c>) and CPU
/// fallback stages, for both <c>r_0 = E mod 2^k == 0</c> and <c>r_0 &gt; 0</c>, and for both probable-prime and composite
/// exponents, emit proofs accepted by <see cref="FermatAlgorithm.VerifyProof"/> in exactly
/// <c>2 * r_0 + 1 + floor(E / 2^k) + 254 * (k + 1)</c> modular squarings while leaving <c>u_K'</c> in
/// <see cref="FermatAlgorithm.GetState"/>.</description></item>
/// <item><description><b>P-rollback-invariance</b>: Gerbicz-Li rollbacks across leaf boundaries and checkpoint restores
/// (in-memory and disk-backed via <see cref="FermatPrpOptions.ProofLeafFile"/>) produce the exact same canonical proof
/// words as a fault-free run.</description></item>
/// <item><description><b>P-soundness-forgery</b>: forging <c>u_E' = u_K * a^10 mod N</c> on a composite modulus satisfies
/// the Fermat congruence <c>u_K * a^10 == u_E'</c> yet is rejected with <c>IsValid == false</c> and
/// <c>IsProbablePrime == false</c>.</description></item>
/// <item><description><b>P-soundness-signflip</b>: odd 128-bit Fiat-Shamir challenges reject order-2 sign-flip forgeries
/// (<c>v -&gt; N - v</c> on <c>u_E</c>, <c>u_K</c> or any <c>mu_t</c>) deterministically.</description></item>
/// <item><description><b>P-domain-guards</b>: 1-bit tampering, zero residues, non-canonical (<c>&gt;= N</c>) residues,
/// mismatched metadata and non-coprime bases are rejected.</description></item>
/// </list>
/// </para>
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class FermatPrpProofTests
{
    // Covers host stage (E = 31, r_0 = 3), GPU stage with r_0 = 0 (E = 32, base 2, k = 3 -> r_0 = 0, leaf A_0 at k = 0),
    // GPU probable primes (E = 61, 87) and GPU composites (E = 41, 63, 95).
    public static TheoryData<int, uint, int> CompletenessCases =>
        new()
        {
            { 31, 3u, 2 },
            { 32, 2u, 3 },
            { 41, 3u, 3 },
            { 61, 3u, 3 },
            { 63, 3u, 3 },
            { 87, 3u, 4 },
            { 95, 3u, 4 },
        };

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(CompletenessCases))]
    public void RunAndVerifyProof_AcrossStagesRemaindersAndVerdicts_VerifiesWithExactSquarings(
        int e,
        uint @base,
        int proofPower)
    {
        using var context = Enter(e);
        var options = new FermatPrpOptions { ProofPower = proofPower };
        using var runner = new FermatAlgorithm(e, @base, options);
        var expected = PrpReference(e, @base);

        var runResult = runner.Run();
        var proof = runner.LastProof;

        // P-completeness: proof exists, matches canonical u_E and u_K, preserves final chain state u_K', and verifies in
        // 2 * r_0 + 1 + d + 254 * (k + 1) squarings.
        // mutant: SquaringChainDriver.cs RunAsync initial `ProofSession?.RecordIfLeaf(k)` at k = 0 removed (fails E = 32, k = 3 where r_0 = 0)
        // mutant: PietrzakProofVerifier.cs Verify `1 + schedule.Remainder` -> `schedule.Remainder` (wrong B_0 exponent)
        Assert.NotNull(proof);
        Assert.Equal(new FermatPrpProofHeader(e, @base, proofPower, runner.ResidueWords), proof.Header);
        Assert.Equal(proofPower, proof.Witnesses.Count);
        Assert.Equal(Squarings(e, @base, e), FromWords(proof.ResidueE.Span));
        Assert.Equal(Squarings(e, @base, 2L * e + 1), FromWords(proof.ResidueK.Span));

        var endIteration = (2L * e + 1 + runner.BlockSize - 1) / runner.BlockSize * runner.BlockSize;
        Assert.Equal(Squarings(e, @base, endIteration), FromWords(runner.GetState()));

        var verification = FermatAlgorithm.VerifyProof(proof);

        var leafCount = 1 << proofPower;
        var stepSize = e / leafCount;
        var remainder = e - leafCount * stepSize;
        var expectedVerifySquarings = 2L * remainder + 1 + stepSize + 254L * (proofPower + 1);

        Assert.True(verification.IsValid, verification.FailureReason);
        Assert.Null(verification.FailureReason);
        Assert.Equal(expected.IsProbablePrime, runResult.IsProbablePrime);
        Assert.Equal(expected.IsProbablePrime, verification.IsProbablePrime);
        Assert.Equal(expected.Residue64, verification.Residue64);
        Assert.Equal(expected.CheckResidue64, verification.CheckResidue64);
        Assert.Equal(expectedVerifySquarings, verification.Squarings);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void RunAndVerifyProof_UnderCpuFallback_MatchesReferenceProof()
    {
        const int E = 41;
        const int ProofPower = 3;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var runner = new FermatAlgorithm(E, 3, new FermatPrpOptions { ProofPower = ProofPower });
            var result = runner.Run();
            var proof = runner.LastProof;

            Assert.NotNull(proof);
            var verification = FermatAlgorithm.VerifyProof(proof);
            var expected = PrpReference(E, 3);

            Assert.True(verification.IsValid, verification.FailureReason);
            Assert.Equal(expected.IsProbablePrime, result.IsProbablePrime);
            Assert.Equal(expected.IsProbablePrime, verification.IsProbablePrime);
            Assert.Equal(expected.Residue64, verification.Residue64);
            Assert.Equal(expected.CheckResidue64, verification.CheckResidue64);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(31, false)]
    [InlineData(41, false)]
    [InlineData(41, true)]
    public void Run_WithFaultInjectionAcrossLeafBoundaries_RecoversExactSameProof(int e, bool useDiskLeafStore)
    {
        using var context = Enter(e);
        var tempLeafPath = useDiskLeafStore
            ? Path.Combine(Path.GetTempPath(), $"gsharp_prp_leaves_{Guid.NewGuid():N}.tmp")
            : null;
        try
        {
            var baseOptions = new FermatPrpOptions
            {
                BlockSize = 5,
                CheckIntervalBlocks = 2,
                ProofPower = 2,
            };

            FermatPrpProof cleanProof;
            using (var cleanRunner = new FermatAlgorithm(e, 3, baseOptions))
            {
                cleanRunner.Run();
                Assert.NotNull(cleanRunner.LastProof);
                cleanProof = cleanRunner.LastProof;
            }

            // Inject errors inside both segments (k = 11 in Segment A, k = 53 in Segment B) so leaves recorded after the
            // last verified checkpoint are corrupted and must be overwritten after Gerbicz-Li rollback.
            // mutant: PietrzakProverSession / IPietrzakLeafStore ignoring overwrites of an already recorded slot
            using var faultyRunner = new FermatAlgorithm(e, 3, baseOptions with { ProofLeafFile = tempLeafPath });
            var pending = new HashSet<long>([11, 53]);
            faultyRunner.CorruptStateAt = pending.Remove;

            var result = faultyRunner.Run();
            var recoveredProof = faultyRunner.LastProof;

            Assert.Equal(2, result.Rollbacks);
            Assert.NotNull(recoveredProof);
            AssertProofsEqual(cleanProof, recoveredProof);

            var verification = FermatAlgorithm.VerifyProof(recoveredProof);
            Assert.True(verification.IsValid, verification.FailureReason);
        }
        finally
        {
            if (tempLeafPath != null && File.Exists(tempLeafPath))
            {
                File.Delete(tempLeafPath);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Restore_WithDiskLeafStoreAndRepeatedRuns_PreservesLeavesAndVerifies()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        const int E = 61;
        var tempLeafPath = Path.Combine(Path.GetTempPath(), $"gsharp_prp_resume_{Guid.NewGuid():N}.leaves");
        var snapshotLeafPath = Path.Combine(Path.GetTempPath(), $"gsharp_prp_resume_snap_{Guid.NewGuid():N}.leaves");
        try
        {
            var checkpoints = new List<FermatPrpCheckpoint>();
            var options = new FermatPrpOptions
            {
                BlockSize = 7,
                ProofPower = 3,
                ProofLeafFile = tempLeafPath,
            };

            FermatPrpProof expectedProof;
            using (var initial = new FermatAlgorithm(E, 3, options with
            {
                OnVerifiedCheckpoint = cp =>
                {
                    checkpoints.Add(cp);
                    if (checkpoints.Count == 1)
                    {
                        // Snapshot the persisted leaf file at the first verified checkpoint (before FinalizeProof folds
                        // leaf slots 0..M_p-1 in place) to simulate resuming after an interrupted run.
                        File.Copy(tempLeafPath, snapshotLeafPath, overwrite: true);
                    }
                }
            }))
            {
                initial.Run();
                Assert.NotNull(initial.LastProof);
                expectedProof = initial.LastProof;
            }

            // Resume a brand-new instance from the mid-chain checkpoint (iteration 49) backed by the persisted leaf file.
            using (var resumed = new FermatAlgorithm(E, 3, options with { ProofLeafFile = snapshotLeafPath }))
            {
                resumed.Restore(checkpoints[0]);
                resumed.Run();
                Assert.NotNull(resumed.LastProof);
                AssertProofsEqual(expectedProof, resumed.LastProof);
                Assert.True(FermatAlgorithm.VerifyProof(resumed.LastProof).IsValid);
            }

            // Resuming a fresh in-memory instance past leaf A_0 without prior leaves must fail fast rather than emit a
            // bogus proof.
            using var missingLeavesRunner = new FermatAlgorithm(E, 3, new FermatPrpOptions { BlockSize = 7, ProofPower = 3 });
            missingLeavesRunner.Restore(checkpoints[0]);
            Assert.Throws<InvalidOperationException>(() => missingLeavesRunner.Run());
        }
        finally
        {
            if (File.Exists(tempLeafPath))
            {
                File.Delete(tempLeafPath);
            }

            if (File.Exists(snapshotLeafPath))
            {
                File.Delete(snapshotLeafPath);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyProof_CompositeForgedToSatisfyFermatCongruence_IsRejectedAndNotProbablePrime()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        const int E = 63; // N(63) is composite for base 3.
        using var runner = new FermatAlgorithm(E, 3, new FermatPrpOptions { ProofPower = 3 });
        var honestRun = runner.Run();
        var honestProof = runner.LastProof;

        Assert.False(honestRun.IsProbablePrime);
        Assert.NotNull(honestProof);

        // Forge u_E' = u_K * 3^10 mod N so that the Fermat PRP congruence u_K * a^10 == u_E' (mod N) holds identically!
        // A broken verifier that only checks u_K * a^10 == u_E or skips Segment A/B binding would call N probable prime.
        var n = ModulusOf(E);
        var uK = FromWords(honestProof.ResidueK.Span);
        var forgedUe = uK * BigInteger.Pow(3, 10) % n;
        var forgedProof = new FermatPrpProof(
            honestProof.Header,
            ToWords(forgedUe, honestProof.ResidueWords),
            honestProof.ResidueK,
            honestProof.Witnesses);

        var verification = FermatAlgorithm.VerifyProof(forgedProof);

        Assert.False(verification.IsValid);
        Assert.False(verification.IsProbablePrime);
        Assert.NotNull(verification.FailureReason);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void VerifyProof_OrderTwoSignFlipAndBitTampering_AreAllRejected()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        const int E = 61;
        const int ProofPower = 3;
        using var runner = new FermatAlgorithm(E, 3, new FermatPrpOptions { ProofPower = ProofPower });
        runner.Run();
        var proof = runner.LastProof;
        Assert.NotNull(proof);

        var n = ModulusOf(E);
        var header = proof.Header;
        var words = proof.ResidueWords;

        // 1. Order-2 sign-flip forgery: v -> N - v on u_K, u_E and every witness mu_t.
        // mutant: PietrzakSchedule.cs ForceMsbAndOdd `| UInt128.One` removed (even challenges allow (-1)^c == 1)
        var negUk = ToWords(n - FromWords(proof.ResidueK.Span), words);
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header, proof.ResidueE, negUk, proof.Witnesses)).IsValid);

        var negUe = ToWords(n - FromWords(proof.ResidueE.Span), words);
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header, negUe, proof.ResidueK, proof.Witnesses)).IsValid);

        for (var t = 0; t < ProofPower; t++)
        {
            var negWitnesses = CloneWitnesses(proof.Witnesses);
            negWitnesses[t] = ToWords(n - FromWords(proof.Witnesses[t].Span), words);
            Assert.False(FermatAlgorithm.VerifyProof(
                new FermatPrpProof(header, proof.ResidueE, proof.ResidueK, negWitnesses)).IsValid);
        }

        // Across deterministic host exponents, replacing the final witness mu_(k-1) with N - mu_(k-1) hits raw SHA-256
        // digests with even LSB; without forcing LSB = 1 on c_(k-1), X_k^(2^d) == Y_k accepts the malleated proof.
        foreach (var hostE in new[] { 17, 19, 21, 23, 25, 27, 29, 31 })
        {
            using var hostRunner = new FermatAlgorithm(hostE, 3, new FermatPrpOptions { ProofPower = 2 });
            hostRunner.Run();
            var hostProof = runnerProofOf(hostRunner);

            var hostModulus = ModulusOf(hostE);
            var malleated = CloneWitnesses(hostProof.Witnesses);
            malleated[^1] = ToWords(hostModulus - FromWords(hostProof.Witnesses[^1].Span), hostProof.ResidueWords);
            var malleatedResult = FermatAlgorithm.VerifyProof(new FermatPrpProof(
                hostProof.Header,
                hostProof.ResidueE,
                hostProof.ResidueK,
                malleated));
            Assert.False(malleatedResult.IsValid, $"Sign-flipped final witness accepted at E = {hostE}.");
        }

        // 2. Single-bit tampering on u_E, u_K and each witness mu_t.
        var flippedUe = FlipLowBitBelowModulus(proof.ResidueE.Span, n, words);
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header, flippedUe, proof.ResidueK, proof.Witnesses)).IsValid);

        var flippedUk = FlipLowBitBelowModulus(proof.ResidueK.Span, n, words);
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header, proof.ResidueE, flippedUk, proof.Witnesses)).IsValid);

        for (var t = 0; t < ProofPower; t++)
        {
            var tamperedWitnesses = CloneWitnesses(proof.Witnesses);
            tamperedWitnesses[t] = FlipLowBitBelowModulus(proof.Witnesses[t].Span, n, words);
            Assert.False(FermatAlgorithm.VerifyProof(
                new FermatPrpProof(header, proof.ResidueE, proof.ResidueK, tamperedWitnesses)).IsValid);
        }

        static FermatPrpProof runnerProofOf(FermatAlgorithm r)
        {
            Assert.NotNull(r.LastProof);
            return r.LastProof;
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void OptionsAndVerifyProof_InvalidParametersZeroResiduesAndSmallDivisors_AreHandledSafely()
    {
        // Invalid ProofPower options throw before allocating any chain buffers.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FermatAlgorithm(31, 3, new FermatPrpOptions { ProofPower = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FermatAlgorithm(31, 3, new FermatPrpOptions { ProofPower = 21 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FermatAlgorithm(7, 3, new FermatPrpOptions { ProofPower = 3 })); // 2^3 = 8 > E = 7
        Assert.Throws<ArgumentException>(() =>
            new FermatAlgorithm(31, 3, new FermatPrpOptions { ProofPower = 2, ProofLeafFile = "   " }));

        // When gcd(a, N) != 1 (E = 32, base 3 -> 3 | N), no chain runs and LastProof stays null.
        using (var divisibleRunner = new FermatAlgorithm(32, 3, new FermatPrpOptions { ProofPower = 3 }))
        {
            var divisibleResult = divisibleRunner.Run();
            Assert.Equal(3u, divisibleResult.SmallDivisor);
            Assert.Null(divisibleRunner.LastProof);
        }

        // Build a valid host-stage proof (E = 31, base 3, k = 2) to test verifier domain guards without needing a GPU.
        using var hostRunner = new FermatAlgorithm(31, 3, new FermatPrpOptions { ProofPower = 2 });
        hostRunner.Run();
        var valid = hostRunner.LastProof;
        Assert.NotNull(valid);

        Assert.Throws<ArgumentNullException>(() => FermatAlgorithm.VerifyProof(null!)); // Intentionally testing null guard

        var header = valid.Header;
        var words = valid.ResidueWords;
        var zero = new uint[words];
        var atModulus = ToWords(ModulusOf(31), words);

        // Zero annihilation guard (mu_0 = 0 would otherwise collapse X <- 0 and Y <- 0 so 0^(2^d) == 0!).
        var zeroWitnesses = CloneWitnesses(valid.Witnesses);
        zeroWitnesses[0] = zero;
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header, valid.ResidueE, valid.ResidueK, zeroWitnesses)).IsValid);

        // Zero or >= N on u_E / u_K.
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header, zero, valid.ResidueK, valid.Witnesses)).IsValid);
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header, atModulus, valid.ResidueK, valid.Witnesses)).IsValid);
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header, valid.ResidueE, atModulus, valid.Witnesses)).IsValid);

        // Wrong witness count, wrong word count, non-coprime base (E = 30, base 3 -> 3 | N), out-of-range ProofPower.
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header, valid.ResidueE, valid.ResidueK, [valid.Witnesses[0]])).IsValid);
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header with { ResidueWords = words + 1 }, valid.ResidueE, valid.ResidueK, valid.Witnesses)).IsValid);
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header with { E = 30 }, valid.ResidueE, valid.ResidueK, valid.Witnesses)).IsValid);
        Assert.False(FermatAlgorithm.VerifyProof(
            new FermatPrpProof(header with { ProofPower = 0 }, valid.ResidueE, valid.ResidueK, [])).IsValid);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SaveAndLoad_RoundTripAndCorruptedFiles_VerifiesAndRejectsTampering()
    {
        using var hostRunner = new FermatAlgorithm(31, 3, new FermatPrpOptions { ProofPower = 2 });
        hostRunner.Run();
        var proof = hostRunner.LastProof;
        Assert.NotNull(proof);

        var tempProofPath = Path.Combine(Path.GetTempPath(), $"henicos_proof_io_{Guid.NewGuid():N}.prp");
        try
        {
            Assert.Throws<ArgumentException>(() => proof.Save("   "));
            Assert.Throws<ArgumentException>(() => FermatPrpProof.Load("   "));
            Assert.Throws<FileNotFoundException>(() => FermatPrpProof.Load(tempProofPath));

            proof.Save(tempProofPath);
            var loaded = FermatPrpProof.Load(tempProofPath);
            AssertProofsEqual(proof, loaded);

            var verified = FermatAlgorithm.VerifyProof(loaded);
            Assert.True(verified.IsValid, verified.FailureReason);
            Assert.True(verified.IsProbablePrime);

            var rawBytes = File.ReadAllBytes(tempProofPath);

            // 1. Flip 1 bit in the payload -> SHA-256 checksum mismatch.
            var payloadFlip = (byte[])rawBytes.Clone();
            payloadFlip[24] ^= 0x01;
            File.WriteAllBytes(tempProofPath, payloadFlip);
            Assert.Throws<InvalidDataException>(() => FermatPrpProof.Load(tempProofPath));

            // 2. Flip 1 bit in the trailing SHA-256 digest -> checksum mismatch.
            var hashFlip = (byte[])rawBytes.Clone();
            hashFlip[^1] ^= 0x80;
            File.WriteAllBytes(tempProofPath, hashFlip);
            Assert.Throws<InvalidDataException>(() => FermatPrpProof.Load(tempProofPath));

            // 3. Truncated file and trailing garbage bytes -> size mismatch.
            File.WriteAllBytes(tempProofPath, rawBytes[..^4]);
            Assert.Throws<InvalidDataException>(() => FermatPrpProof.Load(tempProofPath));

            var extended = new byte[rawBytes.Length + 1];
            rawBytes.CopyTo(extended, 0);
            File.WriteAllBytes(tempProofPath, extended);
            Assert.Throws<InvalidDataException>(() => FermatPrpProof.Load(tempProofPath));

            // 4. Bad magic and unsupported format version.
            var badMagic = (byte[])rawBytes.Clone();
            badMagic[0] = (byte)'X';
            File.WriteAllBytes(tempProofPath, badMagic);
            Assert.Throws<InvalidDataException>(() => FermatPrpProof.Load(tempProofPath));

            var badVersion = (byte[])rawBytes.Clone();
            badVersion[4] = 99;
            File.WriteAllBytes(tempProofPath, badVersion);
            Assert.Throws<InvalidDataException>(() => FermatPrpProof.Load(tempProofPath));
        }
        finally
        {
            if (File.Exists(tempProofPath))
            {
                File.Delete(tempProofPath);
            }
        }
    }

    private static IDisposable? Enter(int e)
    {
        if (e <= 31)
        {
            return null;
        }

        CudaCapability.EnsureGpuAvailable();
        return new GpuKernelLoader.CudaContextScope();
    }

    private static ReadOnlyMemory<uint>[] CloneWitnesses(IReadOnlyList<ReadOnlyMemory<uint>> witnesses)
    {
        var copy = new ReadOnlyMemory<uint>[witnesses.Count];
        for (var i = 0; i < witnesses.Count; i++)
        {
            copy[i] = witnesses[i].ToArray();
        }

        return copy;
    }

    private static uint[] FlipLowBitBelowModulus(ReadOnlySpan<uint> source, BigInteger modulus, int words)
    {
        var copy = source.ToArray();
        copy[0] ^= 1u;
        var value = FromWords(copy);
        if (value.IsZero || value >= modulus)
        {
            copy[0] ^= 3u;
        }

        Assert.Equal(words, copy.Length);
        return copy;
    }

    private static void AssertProofsEqual(FermatPrpProof expected, FermatPrpProof actual)
    {
        Assert.Equal(expected.Header, actual.Header);
        Assert.True(expected.ResidueE.Span.SequenceEqual(actual.ResidueE.Span));
        Assert.True(expected.ResidueK.Span.SequenceEqual(actual.ResidueK.Span));
        Assert.Equal(expected.Witnesses.Count, actual.Witnesses.Count);
        for (var i = 0; i < expected.Witnesses.Count; i++)
        {
            Assert.True(expected.Witnesses[i].Span.SequenceEqual(actual.Witnesses[i].Span));
        }
    }
}
