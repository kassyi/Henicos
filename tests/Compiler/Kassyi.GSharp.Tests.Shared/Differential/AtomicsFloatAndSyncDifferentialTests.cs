#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for floating-point atomics (powers-of-two, Higham error bounds, special IEEE-754 values)
/// and synchronization primitives (CAS spinlock, cooperative lock-free stack).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.AtomicsDifferential)]
public class AtomicsFloatAndSyncDifferentialTests
{
    private static readonly DifferentialCase s_floatExactPowersOfTwoAtomics =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.FloatExactPowersOfTwoAtomicKernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(64, blockX: 64),
            ],
            Inputs = static (_, s) => new DiffBuffers()
                .In("summaryFloats", [2.0f, 1.0f, 0.0f, 0.0f], OutputPolicy.Exact)
                .In("exchSlot", [-1.5f], OutputPolicy.Ignore)
                .Out<float>("oldAddMultisetOut", s.ThreadCount, OutputPolicy.Multiset)
                .Out<float>("oldExchOut", s.ThreadCount, OutputPolicy.Ignore)
                .In(
                    "packets",
                    [new DiffAtomicPacket { Counter = 0, Flags = 0u, Accumulator = 4.0f }],
                    OutputPolicy.Exact),
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchFloatExactPowersOfTwoAtomicKernel(
                b.Get<float>("summaryFloats"),
                b.Get<float>("exchSlot"),
                b.Get<float>("oldAddMultisetOut"),
                b.Get<float>("oldExchOut"),
                b.Get<DiffAtomicPacket>("packets"),
                dynLen: 4,
                gridDimX: s.GridX,
                blockDimX: s.BlockX,
                sharedMemBytes: 4 * sizeof(float)
            ),
            Reference = static (b, s) =>
            {
                var summaryFloats = b.Get<float>("summaryFloats");
                var oldAddMultisetOut = b.Get<float>("oldAddMultisetOut");
                var packets = b.Get<DiffAtomicPacket>("packets");
                int rows = s.ThreadCount;
                int blocks = (int)s.GridX;

                for (int i = 0; i < rows; i++)
                {
                    oldAddMultisetOut[i] = summaryFloats[0];
                    summaryFloats[0] += 0.5f;
                    summaryFloats[1] += 0.25f;
                    packets[0].Accumulator += 0.5f;
                }

                // Each block initializes 4 static slots to 1.0f (= 4.0f per block) and adds 1.5f per thread
                summaryFloats[2] = (blocks * 4.0f) + (rows * 1.5f);
                // Each block initializes 4 dynamic slots to 2.0f (= 8.0f per block) and adds -0.25f per thread
                summaryFloats[3] = (blocks * 8.0f) + (rows * -0.25f);
            },
            VerifyAgainstInitial = static (initial, actual, s) =>
            {
                int rows = s.ThreadCount;
                var oldExch = actual.Get<float>("oldExchOut");
                var expectedSet = new float[rows + 1];
                var actualSet = new float[rows + 1];
                expectedSet[0] = initial.Get<float>("exchSlot")[0];
                actualSet[0] = actual.Get<float>("exchSlot")[0];
                for (int i = 0; i < rows; i++)
                {
                    expectedSet[i + 1] = (i + 1) * 0.5f;
                    actualSet[i + 1] = oldExch[i];
                }

                Array.Sort(expectedSet);
                Array.Sort(actualSet);
                if (!expectedSet.AsSpan().SequenceEqual(actualSet))
                {
                    return PropertyResult.Fail("AtomicExch(float) conservation multiset mismatch.");
                }

                return PropertyResult.Pass();
            },
        };

    private static readonly DifferentialCase s_floatTwoStageRoundingBound =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.FloatTwoStageRoundingBoundKernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(128, blockX: 64),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var input = new float[rows];
                for (int i = 0; i < rows; i++)
                {
                    // Positive normal values in [0.5f, 2.0f) with non-zero low mantissa bits
                    // so rounding depends on order
                    uint mantissa = rng.IsBoundarySeed
                        ? (uint)((i * 2654435761u) & 0x007FFFFFu)
                        : (rng.NextUInt32() & 0x007FFFFFu);
                    uint exp = (i & 1) == 0 ? 126u : 127u;
                    input[i] = BitConverter.UInt32BitsToSingle((exp << 23) | mantissa);
                }

                return new DiffBuffers()
                    .In("input", input, OutputPolicy.Exact, readOnly: true)
                    .In("accumulators", [0.0f, 0.0f, 0.0f], OutputPolicy.Ignore);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchFloatTwoStageRoundingBoundKernel(
                b.Get<float>("input"),
                b.Get<float>("accumulators"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            VerifyAgainstInitial = static (initial, actual, s) =>
            {
                var input = initial.Get<float>("input");
                var acc = actual.Get<float>("accumulators");
                int n = s.ThreadCount;

                double exactSum = 0.0;
                double condSum = 0.0;
                for (int i = 0; i < n; i++)
                {
                    exactSum += input[i];
                    condSum += Math.Abs((double)input[i]);
                }

                // Higham forward error bound for N float additions: gamma_N = (N * u) / (1 - N * u), u = 2^-24
                double u = Math.ScaleB(1.0, -24);
                double gammaN = (n * u) / (1.0 - (n * u));
                double maxAllowedError = gammaN * condSum;

                for (int k = 0; k < 3; k++)
                {
                    double err = Math.Abs(acc[k] - exactSum);
                    if (!(err <= maxAllowedError))
                    {
                        return PropertyResult.Fail(
                            $"Accumulator[{k}]={acc[k]:R} exceeded Higham bound: " +
                            $"|err|={err:E4} > {maxAllowedError:E4} (exact={exactSum:R})."
                        );
                    }
                }

                return PropertyResult.Pass();
            },
        };

    private static readonly DifferentialCase s_floatSpecialValuesContention =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.FloatSpecialValuesContentionKernel)}")
        {
            Shape = LaunchShape.ForRows(32, blockX: 32),
            Inputs = static (_, _) =>
            {
                float subnormal = BitConverter.UInt32BitsToSingle(0x00400000u); // largest-bit subnormal (2^-127)
                // Non-canonical quiet NaN with payload
                float nanPayload = BitConverter.UInt32BitsToSingle(0x7FC01234u);
                return new DiffBuffers()
                    .In(
                        "specialInputs",
                        [subnormal, nanPayload, -0.0f, 1.0f, float.NegativeInfinity],
                        OutputPolicy.Exact,
                        readOnly: true)
                    .Out<float>(
                        "specialOutputs",
                        rows: 1,
                        [
                            (AtomicsDifferentialKernels.FloatSpecialColumns[0], OutputPolicy.Exact),
                            (AtomicsDifferentialKernels.FloatSpecialColumns[1], OutputPolicy.Exact),
                            (AtomicsDifferentialKernels.FloatSpecialColumns[2], OutputPolicy.Exact),
                            (AtomicsDifferentialKernels.FloatSpecialColumns[3], OutputPolicy.Exact),
                            (AtomicsDifferentialKernels.FloatSpecialColumns[4], OutputPolicy.Exact),
                            (AtomicsDifferentialKernels.FloatSpecialColumns[5], OutputPolicy.Bitwise),
                        ]
                    );
            },
            Launch = static (b, s) =>
            {
                var outBuf = b.Get<float>("specialOutputs");
                outBuf[0] = 0.0f;
                outBuf[1] = 0.0f;
                outBuf[2] = 1.0f;
                outBuf[3] = -0.0f;
                outBuf[4] = float.PositiveInfinity;
                outBuf[5] = float.PositiveInfinity;
                AtomicsDifferentialKernels.LaunchFloatSpecialValuesContentionKernel(
                    b.Get<float>("specialInputs"),
                    outBuf,
                    gridDimX: s.GridX,
                    blockDimX: s.BlockX
                );
            },
            Reference = static (b, s) =>
            {
                var inputs = b.Get<float>("specialInputs");
                var outBuf = b.Get<float>("specialOutputs");
                float subnormal = inputs[0];

                // Column 0: PTX atom.global.add.f32 flushes subnormal inputs to +0.0f (#868)
                outBuf[0] = 0.0f;

                // Column 1: PTX atom.shared.add.f32 preserves subnormal inputs (32 * 2^-127 = 2^-122)
                float sharedAcc = 0.0f;
                for (int i = 0; i < s.ThreadCount; i++)
                {
                    sharedAcc += subnormal;
                }

                outBuf[1] = sharedAcc;

                // Column 2: PTX atom.global.add.f32 canonicalizes NaN to 0x7FFFFFFF (#868)
                outBuf[2] = BitConverter.UInt32BitsToSingle(0x7FFFFFFFu);

                // Column 3: -0.0f + (-0.0f) preserves negative zero (-0.0f, 0x80000000)
                outBuf[3] = -0.0f;

                // Column 4: +Infinity + 1.0f = +Infinity
                outBuf[4] = float.PositiveInfinity;

                // Column 5: +Infinity + (-Infinity) = NaN
                outBuf[5] = float.NaN;
            },
        };

    private static readonly DifferentialCase s_blockLeaderCasSpinlock =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.BlockLeaderCasSpinlockKernel)}")
        {
            Shapes =
            [
                LaunchShapes.Grid1D(4u, 32u),
                LaunchShapes.Grid1D(8u, 32u),
            ],
            Inputs = static (rng, s) =>
            {
                int blocks = (int)s.GridX;
                var payloads = new int[blocks];
                for (int i = 0; i < blocks; i++)
                {
                    payloads[i] = (DiffValues.NextInt32(rng) & 0x3FFF) + 1;
                }

                return new DiffBuffers()
                    .In("blockPayloads", payloads, OutputPolicy.Exact, readOnly: true)
                    .In("lockSlot", [0], OutputPolicy.Exact)
                    .In("sharedState", [0, 100, 0x13572468], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchBlockLeaderCasSpinlockKernel(
                b.Get<int>("blockPayloads"),
                b.Get<int>("lockSlot"),
                b.Get<int>("sharedState"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var payloads = b.Get<int>("blockPayloads");
                var lockSlot = b.Get<int>("lockSlot");
                var sharedState = b.Get<int>("sharedState");
                int blocks = (int)s.GridX;

                for (int blockId = 0; blockId < blocks; blockId++)
                {
                    int p = payloads[blockId];
                    sharedState[0] += 1;
                    sharedState[1] += p;
                    sharedState[2] ^= (p * 3 + 0x5A5A);
                }

                lockSlot[0] = 0;
            },
        };

    private static readonly DifferentialCase s_cooperativeCasLockFreeStack =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.CooperativeCasLockFreeStackKernel)}")
        {
            Shapes =
            [
                LaunchShapes.Grid1D(2u, 32u),
                LaunchShapes.Grid1D(4u, 32u),
            ],
            Inputs = static (_, s) =>
            {
                int rows = s.ThreadCount;
                var next = new int[rows];
                Array.Fill(next, -1);
                return new DiffBuffers()
                    .In("head", [-1], OutputPolicy.Exact)
                    .In("next", next, OutputPolicy.Ignore)
                    .Out<int>("popped", rows, OutputPolicy.Multiset);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchCooperativeCasLockFreeStackKernel(
                b.Get<int>("head"),
                b.Get<int>("next"),
                b.Get<int>("popped"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var head = b.Get<int>("head");
                var popped = b.Get<int>("popped");
                int rows = s.ThreadCount;
                for (int i = 0; i < rows; i++)
                {
                    popped[i] = i;
                }

                head[0] = -1;
            },
            Verify = static (actual, s) =>
            {
                var head = actual.Get<int>("head");
                var popped = actual.Get<int>("popped");
                int rows = s.ThreadCount;
                if (head[0] != -1)
                {
                    return PropertyResult.Fail($"Expected stack head -1 after all pops, got {head[0]}.");
                }

                var seen = new bool[rows];
                for (int i = 0; i < rows; i++)
                {
                    int node = popped[i];
                    if ((uint)node >= (uint)rows)
                    {
                        return PropertyResult.Fail($"Popped out-of-range node index {node} at thread {i}.");
                    }

                    if (seen[node])
                    {
                        return PropertyResult.Fail(
                            $"Duplicate popped node {node} at thread {i} (ABA or lost CAS update).");
                    }

                    seen[node] = true;
                }

                return PropertyResult.Pass();
            },
        };

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void FloatAtomics_ExactPowersOfTwo_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_floatExactPowersOfTwoAtomics);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FloatTwoStageAtomics_RoundingBound_SatisfiesHighamForwardErrorBoundOnEveryPath() =>
        DifferentialRunner.Run(s_floatTwoStageRoundingBound);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FloatAtomics_SpecialValuesContention_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_floatSpecialValuesContention);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CasSpinlock_BlockLeaderContention_ProtectsMultiWordCriticalSectionOnEveryPath() =>
        DifferentialRunner.Run(s_blockLeaderCasSpinlock);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LockFreeStack_CooperativeCasAcrossGridSync_PushesAndPopsBijectionAcrossGridSync() =>
        DifferentialRunner.Run(s_cooperativeCasLockFreeStack);
}
