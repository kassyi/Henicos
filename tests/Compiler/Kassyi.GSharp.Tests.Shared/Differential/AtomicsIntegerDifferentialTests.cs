#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for integer atomics (<see cref="Gpu.AtomicAdd(ref int, int)"/>, exchange, CAS, bitwise atomics).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.AtomicsDifferential)]
public class AtomicsIntegerDifferentialTests
{
    private static readonly DifferentialCase s_integerSameStepAtomics =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.IntegerSameStepAtomicsKernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(64, blockX: 64),
            ],
            Inputs = static (_, rows) => new DiffBuffers()
                .In("stateInt", [100, 1000, 50, 10, 20, 30, 40], OutputPolicy.Exact)
                .In("stateUInt", [200u, 2000u, 60u, 0u, 7u, 100u, 200u, 300u, 400u], OutputPolicy.Exact)
                .In("stateULong", [300UL, 70UL], OutputPolicy.Exact)
                .Out<int>("oldValuesOut", rows, AtomicsDifferentialKernels.SameStepOldColumns, OutputPolicy.Multiset)
                .Out<ulong>("oldULongOut", rows, OutputPolicy.Multiset),
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerSameStepAtomicsKernel(
                b.Get<int>("stateInt"),
                b.Get<uint>("stateUInt"),
                b.Get<ulong>("stateULong"),
                b.Get<int>("oldValuesOut"),
                b.Get<ulong>("oldULongOut"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var stateInt = b.Get<int>("stateInt");
                var stateUInt = b.Get<uint>("stateUInt");
                var stateULong = b.Get<ulong>("stateULong");
                var oldValuesOut = b.Get<int>("oldValuesOut");
                var oldULongOut = b.Get<ulong>("oldULongOut");
                int rows = s.ThreadCount;

                for (int tid = 0; tid < rows; tid++)
                {
                    int bucket = tid & 3;

                    int oldAddI32 = stateInt[0];
                    stateInt[0] = unchecked(stateInt[0] + 3);

                    int oldSubI32 = stateInt[1];
                    stateInt[1] = unchecked(stateInt[1] - 2);

                    stateInt[2] = unchecked(stateInt[2] + 5);
                    stateInt[3 + bucket] = unchecked(stateInt[3 + bucket] + 7);

                    uint oldAddU32 = stateUInt[0];
                    stateUInt[0] = unchecked(stateUInt[0] + 4u);

                    uint oldSubU32 = stateUInt[1];
                    stateUInt[1] = unchecked(stateUInt[1] - 3u);

                    stateUInt[2] = unchecked(stateUInt[2] + 6u);

                    uint oldIncU32 = stateUInt[3];
                    stateUInt[3] = oldIncU32 >= 7u ? 0u : oldIncU32 + 1u;

                    uint oldDecU32 = stateUInt[4];
                    stateUInt[4] = (oldDecU32 == 0u || oldDecU32 > 7u) ? 7u : oldDecU32 - 1u;

                    stateUInt[5 + bucket] = unchecked(stateUInt[5 + bucket] - 2u);

                    ulong oldAddU64 = stateULong[0];
                    stateULong[0] = unchecked(stateULong[0] + 11UL);
                    stateULong[1] = unchecked(stateULong[1] + 13UL);

                    int o = tid * 6;
                    oldValuesOut[o + 0] = oldAddI32;
                    oldValuesOut[o + 1] = oldSubI32;
                    oldValuesOut[o + 2] = (int)oldAddU32;
                    oldValuesOut[o + 3] = (int)oldSubU32;
                    oldValuesOut[o + 4] = (int)oldIncU32;
                    oldValuesOut[o + 5] = (int)oldDecU32;
                    oldULongOut[tid] = oldAddU64;
                }
            },
        };

    private static readonly DifferentialCase s_integerArbitraryAtomicsInt32 =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.IntegerArbitraryAtomicsInt32Kernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(64, blockX: 64),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inInt = DiffValues.Int32(rng, rows);
                var packets = new DiffAtomicPacket[4];
                for (int i = 0; i < 4; i++)
                {
                    packets[i] = new DiffAtomicPacket { Counter = 10 * (i + 1), Flags = 0u, Accumulator = 0f };
                }

                return new DiffBuffers()
                    .In("inInt", inInt, OutputPolicy.Exact, readOnly: true)
                    .In("stateInt", [50, 500, int.MaxValue, -1, 0x12345678, -999999, 25, 777777], OutputPolicy.Ignore)
                    .Out<int>("oldExchAndCasInt", rows * 2, OutputPolicy.Ignore)
                    .In("packets", packets, OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerArbitraryAtomicsInt32Kernel(
                b.Get<int>("inInt"),
                b.Get<int>("stateInt"),
                b.Get<int>("oldExchAndCasInt"),
                b.Get<DiffAtomicPacket>("packets"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var inInt = b.Get<int>("inInt");
                var packets = b.Get<DiffAtomicPacket>("packets");
                int rows = s.ThreadCount;

                for (int tid = 0; tid < rows; tid++)
                {
                    int bucket = tid & 3;
                    int vInt = inInt[tid];
                    packets[bucket].Counter += (vInt & 0xFF) + 1;
                    packets[bucket].Flags |= 1u << (tid & 31);
                }
            },
            VerifyAgainstInitial = static (initial, actual, s) =>
            {
                int rows = s.ThreadCount;
                var inInt = initial.Get<int>("inInt");
                var initState = initial.Get<int>("stateInt");
                var actualState = actual.Get<int>("stateInt");

                int s0 = initState[0], s1 = initState[1], s2 = initState[2];
                int s3 = initState[3], s4 = initState[4], s6 = initState[6];
                for (int i = 0; i < rows; i++)
                {
                    int v = inInt[i];
                    s0 = unchecked(s0 + v);
                    s1 = unchecked(s1 - v);
                    if (v < s2) s2 = v;
                    s3 &= v;
                    s4 ^= v;
                    s6 = unchecked(s6 + v);
                }

                if (actualState[0] != s0 || actualState[1] != s1 || actualState[2] != s2 ||
                    actualState[3] != s3 || actualState[4] != s4 || actualState[5] != 100000 || actualState[6] != s6)
                {
                    return PropertyResult.Fail("Deterministic state mismatch in IntegerArbitraryAtomicsInt32.");
                }

                return AtomicsDifferentialTestHelpers.CheckExchangeAndCas<int>(
                    initial, actual, rows, "inInt", "oldExchAndCasInt", "stateInt", 7, -999999, 100000);
            },
        };

    private static readonly DifferentialCase s_integerArbitraryAtomicsUInt32 =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.IntegerArbitraryAtomicsUInt32Kernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(64, blockX: 64),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inUInt = new uint[rows];
                for (int i = 0; i < rows; i++)
                {
                    inUInt[i] = rng.IsBoundarySeed ? unchecked((uint)rng.NextUInt32()) : rng.NextUInt32();
                }

                return new DiffBuffers()
                    .In("inUInt", inUInt, OutputPolicy.Exact, readOnly: true)
                    .In(
                        "stateUInt",
                        [
                            100u, 1000u, uint.MaxValue, 0u, 0xFFFFFFFFu,
                            0u, 0xA5A5A5A5u, 0xCAFEBABEu, uint.MaxValue, 0x88888888u,
                        ],
                        OutputPolicy.Ignore)
                    .Out<uint>("oldExchAndCasUInt", rows * 2, OutputPolicy.Ignore);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerArbitraryAtomicsUInt32Kernel(
                b.Get<uint>("inUInt"),
                b.Get<uint>("stateUInt"),
                b.Get<uint>("oldExchAndCasUInt"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (_, _) => { },
            VerifyAgainstInitial = static (initial, actual, s) =>
            {
                int rows = s.ThreadCount;
                var inUInt = initial.Get<uint>("inUInt");
                var initState = initial.Get<uint>("stateUInt");
                var actualState = actual.Get<uint>("stateUInt");

                uint s0 = initState[0], s1 = initState[1], s2 = initState[2], s3 = initState[3];
                uint s4 = initState[4], s5 = initState[5], s6 = initState[6], s8 = initState[8];
                for (int i = 0; i < rows; i++)
                {
                    uint v = inUInt[i];
                    s0 = unchecked(s0 + v);
                    s1 = unchecked(s1 - v);
                    if (v < s2) s2 = v;
                    if (v > s3) s3 = v;
                    s4 &= v;
                    s5 |= v;
                    s6 ^= v;
                    if (v < s8) s8 = v;
                }

                if (actualState[0] != s0 || actualState[1] != s1 || actualState[2] != s2 || actualState[3] != s3 ||
                    actualState[4] != s4 || actualState[5] != s5 || actualState[6] != s6 ||
                    actualState[7] != 0x12345678u || actualState[8] != s8)
                {
                    return PropertyResult.Fail("Deterministic state mismatch in IntegerArbitraryAtomicsUInt32.");
                }

                return AtomicsDifferentialTestHelpers.CheckExchangeAndCas<uint>(
                    initial, actual, rows, "inUInt", "oldExchAndCasUInt", "stateUInt", 9, 0xCAFEBABEu, 0x12345678u);
            },
        };

    private static readonly DifferentialCase s_integerArbitraryAtomicsUInt64 =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.IntegerArbitraryAtomicsUInt64Kernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(64, blockX: 64),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inULong = new ulong[rows];
                for (int i = 0; i < rows; i++)
                {
                    inULong[i] = rng.IsBoundarySeed
                        ? ((ulong)rng.NextUInt32() << 32) ^ (uint)i
                        : ((ulong)rng.NextUInt32() << 32) | rng.NextUInt32();
                }

                return new DiffBuffers()
                    .In("inULong", inULong, OutputPolicy.Exact, readOnly: true)
                    .In(
                        "stateULong",
                        [
                            200UL, ulong.MaxValue, 0UL, ulong.MaxValue, 0UL,
                            0x0F0F0F0F_F0F0F0F0UL, 0xDEADBEEF_CAFEBABEUL, 0UL, 0x99999999_AAAAAAAAUL,
                        ],
                        OutputPolicy.Ignore)
                    .Out<ulong>("oldExchAndCasULong", rows * 2, OutputPolicy.Ignore);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerArbitraryAtomicsUInt64Kernel(
                b.Get<ulong>("inULong"),
                b.Get<ulong>("stateULong"),
                b.Get<ulong>("oldExchAndCasULong"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (_, _) => { },
            VerifyAgainstInitial = static (initial, actual, s) =>
            {
                int rows = s.ThreadCount;
                var inULong = initial.Get<ulong>("inULong");
                var initState = initial.Get<ulong>("stateULong");
                var actualState = actual.Get<ulong>("stateULong");

                ulong s0 = initState[0], s1 = initState[1], s2 = initState[2], s3 = initState[3];
                ulong s4 = initState[4], s5 = initState[5], s7 = initState[7];
                for (int i = 0; i < rows; i++)
                {
                    ulong v = inULong[i];
                    s0 = unchecked(s0 + v);
                    if (v < s1) s1 = v;
                    if (v > s2) s2 = v;
                    s3 &= v;
                    s4 |= v;
                    s5 ^= v;
                    s7 ^= v;
                }

                if (actualState[0] != s0 || actualState[1] != s1 || actualState[2] != s2 ||
                    actualState[3] != s3 || actualState[4] != s4 || actualState[5] != s5 ||
                    actualState[6] != 0x01020304_05060708UL || actualState[7] != s7)
                {
                    return PropertyResult.Fail("Deterministic state mismatch in IntegerArbitraryAtomicsUInt64.");
                }

                return AtomicsDifferentialTestHelpers.CheckExchangeAndCas<ulong>(
                    initial, actual, rows, "inULong", "oldExchAndCasULong", "stateULong",
                    8, 0xDEADBEEF_CAFEBABEUL, 0x01020304_05060708UL);
            },
        };

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_IntegerSameStepAtomics_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integerSameStepAtomics);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerArbitraryAtomicsInt32_MatchesTheReferenceAndConservationProperties() =>
        DifferentialRunner.Run(s_integerArbitraryAtomicsInt32);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerArbitraryAtomicsUInt32_MatchesTheReferenceAndConservationProperties() =>
        DifferentialRunner.Run(s_integerArbitraryAtomicsUInt32);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerArbitraryAtomicsUInt64_MatchesTheReferenceAndConservationProperties() =>
        DifferentialRunner.Run(s_integerArbitraryAtomicsUInt64);
}
