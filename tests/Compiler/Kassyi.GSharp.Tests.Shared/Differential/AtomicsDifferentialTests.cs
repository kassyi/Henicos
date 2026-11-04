#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="Gpu"/> atomics, <see cref="System.Threading.Interlocked"/> intrinsics,
/// shared-memory atomics, struct-field atomics, CAS spinlocks, and cooperative lock-free stacks.
/// </summary>
public class AtomicsDifferentialTests
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

    private static PropertyResult CheckExchangeAndCas<T>(
        DiffBuffers initial,
        DiffBuffers actual,
        int rows,
        string inputName,
        string oldName,
        string stateName,
        int slotIndex,
        T casExpected,
        T casValue)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var input = initial.Get<T>(inputName);
        var old = actual.Get<T>(oldName);
        var stateInitial = initial.Get<T>(stateName);
        var stateActual = actual.Get<T>(stateName);

        var expectedSet = new T[rows + 1];
        var actualSet = new T[rows + 1];
        expectedSet[0] = stateInitial[slotIndex];
        actualSet[0] = stateActual[slotIndex];

        int casWinners = 0;
        for (int i = 0; i < rows; i++)
        {
            expectedSet[i + 1] = input[i];
            actualSet[i + 1] = old[i * 2 + 0];
            T casOld = old[i * 2 + 1];
            if (casOld.Equals(casExpected))
            {
                casWinners++;
            }
            else if (!casOld.Equals(casValue))
            {
                return PropertyResult.Fail($"Unexpected {typeof(T).Name} CAS old value {casOld} at thread {i}.");
            }
        }

        Array.Sort(expectedSet);
        Array.Sort(actualSet);
        if (!expectedSet.AsSpan().SequenceEqual(actualSet))
        {
            return PropertyResult.Fail($"AtomicExch({typeof(T).Name}) conservation multiset mismatch.");
        }

        if (casWinners != 1)
        {
            return PropertyResult.Fail($"Expected exactly 1 {typeof(T).Name} CAS winner, observed {casWinners}.");
        }

        return PropertyResult.Pass();
    }

    private static readonly DifferentialCase s_integerArbitraryAtomicsInt32 =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.IntegerArbitraryAtomicsInt32Kernel)}")
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

                int s0 = initState[0], s1 = initState[1], s2 = initState[2], s3 = initState[3], s4 = initState[4], s6 = initState[6];
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

                return CheckExchangeAndCas<int>(initial, actual, rows, "inInt", "oldExchAndCasInt", "stateInt", 7, -999999, 100000);
            },
        };

    private static readonly DifferentialCase s_integerArbitraryAtomicsUInt32 =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.IntegerArbitraryAtomicsUInt32Kernel)}")
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
                    .In("stateUInt", [100u, 1000u, uint.MaxValue, 0u, 0xFFFFFFFFu, 0u, 0xA5A5A5A5u, 0xCAFEBABEu, uint.MaxValue, 0x88888888u], OutputPolicy.Ignore)
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
                    actualState[4] != s4 || actualState[5] != s5 || actualState[6] != s6 || actualState[7] != 0x12345678u || actualState[8] != s8)
                {
                    return PropertyResult.Fail("Deterministic state mismatch in IntegerArbitraryAtomicsUInt32.");
                }

                return CheckExchangeAndCas<uint>(initial, actual, rows, "inUInt", "oldExchAndCasUInt", "stateUInt", 9, 0xCAFEBABEu, 0x12345678u);
            },
        };

    private static readonly DifferentialCase s_integerArbitraryAtomicsUInt64 =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.IntegerArbitraryAtomicsUInt64Kernel)}")
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
                    .In("stateULong", [200UL, ulong.MaxValue, 0UL, ulong.MaxValue, 0UL, 0x0F0F0F0F_F0F0F0F0UL, 0xDEADBEEF_CAFEBABEUL, 0UL, 0x99999999_AAAAAAAAUL], OutputPolicy.Ignore)
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

                return CheckExchangeAndCas<ulong>(initial, actual, rows, "inULong", "oldExchAndCasULong", "stateULong", 8, 0xDEADBEEF_CAFEBABEUL, 0x01020304_05060708UL);
            },
        };

    private static readonly DifferentialCase s_integerSharedMemoryTwoStageAtomicsInt32 =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.IntegerSharedMemoryTwoStageAtomicsInt32Kernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(128, blockX: 64),
            ],
            Inputs = static (rng, s) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, s.ThreadCount), OutputPolicy.Exact, readOnly: true)
                .Out<int>("threadOutput", s.ThreadCount, AtomicsDifferentialKernels.SharedTwoStageThreadInt32Columns, OutputPolicy.Exact)
                .In("globalSummary", [0, int.MaxValue, 0, 0], OutputPolicy.Exact),
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerSharedMemoryTwoStageAtomicsInt32Kernel(
                b.Get<int>("input"),
                b.Get<int>("threadOutput"),
                b.Get<int>("globalSummary"),
                dynSize: 8,
                gridDimX: s.GridX,
                blockDimX: s.BlockX,
                sharedMemBytes: 8 * sizeof(int)
            ),
            Reference = static (b, s) =>
            {
                var input = b.Get<int>("input");
                var threadOutput = b.Get<int>("threadOutput");
                var globalSummary = b.Get<int>("globalSummary");
                int blockSize = (int)s.BlockX;
                int totalThreads = s.ThreadCount;

                Span<int> sAdd = stackalloc int[8];
                Span<int> sSub = stackalloc int[8];
                Span<int> sMin = stackalloc int[8];
                Span<int> sAnd = stackalloc int[8];
                Span<int> sXor = stackalloc int[8];
                Span<int> sCas = stackalloc int[4];
                Span<int> sDyn = stackalloc int[8];

                for (int blockStart = 0; blockStart < totalThreads; blockStart += blockSize)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        sAdd[i] = 100;
                        sSub[i] = 1000;
                        sMin[i] = int.MaxValue;
                        sAnd[i] = -1;
                        sXor[i] = 0;
                        sDyn[i] = 50;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        sCas[i] = -1;
                    }

                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = blockStart + localTid;
                        int bucket = localTid & 7;
                        int v = (input[gid] & 0x7FFF) - 0x3FFF;

                        sAdd[bucket] += v;
                        sSub[bucket] -= v;
                        if (v < sMin[bucket])
                        {
                            sMin[bucket] = v;
                        }

                        sAnd[bucket] &= (v | 0x101);
                        sXor[bucket] ^= v;
                        sCas[localTid & 3] = 777;
                        sDyn[bucket] += v + localTid;
                    }

                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = blockStart + localTid;
                        int bucket = localTid & 7;
                        int o = gid * 7;
                        threadOutput[o + 0] = sAdd[bucket];
                        threadOutput[o + 1] = sSub[bucket];
                        threadOutput[o + 2] = sMin[bucket];
                        threadOutput[o + 3] = sAnd[bucket];
                        threadOutput[o + 4] = sXor[bucket];
                        threadOutput[o + 5] = sCas[localTid & 3];
                        threadOutput[o + 6] = sDyn[bucket];
                    }

                    int blockAddTotal = 0;
                    int blockMin = int.MaxValue;
                    int blockXor = 0;
                    int blockDynTotal = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        blockAddTotal += sAdd[i];
                        if (sMin[i] < blockMin)
                        {
                            blockMin = sMin[i];
                        }

                        blockXor ^= sXor[i];
                        blockDynTotal += sDyn[i];
                    }

                    globalSummary[0] += blockAddTotal;
                    if (blockMin < globalSummary[1])
                    {
                        globalSummary[1] = blockMin;
                    }

                    globalSummary[2] ^= blockXor;
                    globalSummary[3] += blockDynTotal;
                }
            },
        };

    private static readonly DifferentialCase s_integerSharedMemoryTwoStageAtomicsUInt32 =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.IntegerSharedMemoryTwoStageAtomicsUInt32Kernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(128, blockX: 64),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inUInt = new uint[rows];
                for (int i = 0; i < rows; i++)
                {
                    inUInt[i] = rng.NextUInt32();
                }

                return new DiffBuffers()
                    .In("input", inUInt, OutputPolicy.Exact, readOnly: true)
                    .Out<uint>("threadOutput", rows, AtomicsDifferentialKernels.SharedTwoStageThreadUInt32Columns, OutputPolicy.Exact)
                    .In("globalSummary", [0u], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerSharedMemoryTwoStageAtomicsUInt32Kernel(
                b.Get<uint>("input"),
                b.Get<uint>("threadOutput"),
                b.Get<uint>("globalSummary"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var input = b.Get<uint>("input");
                var threadOutput = b.Get<uint>("threadOutput");
                var globalSummary = b.Get<uint>("globalSummary");
                int blockSize = (int)s.BlockX;
                int totalThreads = s.ThreadCount;

                Span<uint> sMaxU = stackalloc uint[8];
                Span<uint> sOrU = stackalloc uint[8];

                for (int blockStart = 0; blockStart < totalThreads; blockStart += blockSize)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        sMaxU[i] = 0u;
                        sOrU[i] = 0u;
                    }

                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = blockStart + localTid;
                        int bucket = localTid & 7;
                        uint vu = (uint)(input[gid] & 0xFFFF);

                        if (vu > sMaxU[bucket])
                        {
                            sMaxU[bucket] = vu;
                        }

                        sOrU[bucket] |= vu;
                    }

                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = blockStart + localTid;
                        int bucket = localTid & 7;
                        int o = gid * 2;
                        threadOutput[o + 0] = sMaxU[bucket];
                        threadOutput[o + 1] = sOrU[bucket];
                    }

                    uint blockMaxU = 0u;
                    for (int i = 0; i < 8; i++)
                    {
                        if (sMaxU[i] > blockMaxU)
                        {
                            blockMaxU = sMaxU[i];
                        }
                    }

                    if (blockMaxU > globalSummary[0])
                    {
                        globalSummary[0] = blockMaxU;
                    }
                }
            },
        };

    private static readonly DifferentialCase s_integerSharedMemoryTwoStageAtomicsUInt64 =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.IntegerSharedMemoryTwoStageAtomicsUInt64Kernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(128, blockX: 64),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inULong = new ulong[rows];
                for (int i = 0; i < rows; i++)
                {
                    inULong[i] = ((ulong)rng.NextUInt32() << 32) | rng.NextUInt32();
                }

                return new DiffBuffers()
                    .In("input", inULong, OutputPolicy.Exact, readOnly: true)
                    .Out<ulong>("threadOutput", rows, AtomicsDifferentialKernels.SharedTwoStageThreadUInt64Columns, OutputPolicy.Exact)
                    .In("globalSummary", [0UL], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerSharedMemoryTwoStageAtomicsUInt64Kernel(
                b.Get<ulong>("input"),
                b.Get<ulong>("threadOutput"),
                b.Get<ulong>("globalSummary"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var input = b.Get<ulong>("input");
                var threadOutput = b.Get<ulong>("threadOutput");
                var globalSummary = b.Get<ulong>("globalSummary");
                int blockSize = (int)s.BlockX;
                int totalThreads = s.ThreadCount;

                Span<ulong> sU64 = stackalloc ulong[4];

                for (int blockStart = 0; blockStart < totalThreads; blockStart += blockSize)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        sU64[i] = 0UL;
                    }

                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = blockStart + localTid;
                        ulong vu64 = input[gid];
                        sU64[localTid & 3] += vu64;
                    }

                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = blockStart + localTid;
                        threadOutput[gid] = sU64[localTid & 3];
                    }

                    ulong blockU64Xor = sU64[0] ^ sU64[1] ^ sU64[2] ^ sU64[3];
                    globalSummary[0] ^= blockU64Xor;
                }
            },
        };

    private static readonly DifferentialCase s_interlockedInt32Overloads =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.InterlockedInt32OverloadsKernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inInt = DiffValues.Int32(rng, rows);
                var uncontendedInt = new int[rows * 3];

                for (int i = 0; i < rows; i++)
                {
                    uncontendedInt[i * 3 + 0] = 100 + i;
                    uncontendedInt[i * 3 + 1] = 200 + i;
                    uncontendedInt[i * 3 + 2] = 300 + i;
                }

                return new DiffBuffers()
                    .In("input", inInt, OutputPolicy.Exact, readOnly: true)
                    .In("uncontendedSlots", uncontendedInt, OutputPolicy.Exact)
                    .Out<int>("returnsOut", rows, AtomicsDifferentialKernels.InterlockedInt32Columns, OutputPolicy.Exact)
                    .In("contendedState", [50, -111111, 0], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchInterlockedInt32OverloadsKernel(
                b.Get<int>("input"),
                b.Get<int>("uncontendedSlots"),
                b.Get<int>("returnsOut"),
                b.Get<int>("contendedState"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var inInt = b.Get<int>("input");
                var uncontendedInt = b.Get<int>("uncontendedSlots");
                var returnsOut = b.Get<int>("returnsOut");
                var contendedInt = b.Get<int>("contendedState");

                int rows = s.ThreadCount;
                int blockSize = (int)s.BlockX;
                int blocks = rows / blockSize;

                for (int tid = 0; tid < rows; tid++)
                {
                    int v = (inInt[tid] & 0x3FFF) - 0x1FFF;

                    uncontendedInt[tid * 3 + 0] += v;
                    int retAddI32 = uncontendedInt[tid * 3 + 0];

                    int retExchI32 = uncontendedInt[tid * 3 + 1];
                    uncontendedInt[tid * 3 + 1] = v + 500;

                    int retCasHitI32 = uncontendedInt[tid * 3 + 2];
                    uncontendedInt[tid * 3 + 2] = v + 900;
                    int retCasMissI32 = uncontendedInt[tid * 3 + 2];

                    contendedInt[0] += v;
                    contendedInt[2] += v;

                    int o = tid * 4;
                    returnsOut[o + 0] = retAddI32;
                    returnsOut[o + 1] = retExchI32;
                    returnsOut[o + 2] = retCasHitI32;
                    returnsOut[o + 3] = retCasMissI32;
                }

                contendedInt[1] = 424242;
                contendedInt[2] += blocks * 4 * 1000;
            },
        };

    private static readonly DifferentialCase s_interlockedUInt32Overloads =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.InterlockedUInt32OverloadsKernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inUInt = new uint[rows];
                var uncontendedUInt = new uint[rows * 2];

                for (int i = 0; i < rows; i++)
                {
                    inUInt[i] = (uint)(rng.NextUInt32() & 0xFFFF) + 1u;
                    uncontendedUInt[i * 2 + 0] = (uint)(400 + i);
                    uncontendedUInt[i * 2 + 1] = (uint)(500 + i);
                }

                return new DiffBuffers()
                    .In("input", inUInt, OutputPolicy.Exact, readOnly: true)
                    .In("uncontendedSlots", uncontendedUInt, OutputPolicy.Exact)
                    .Out<uint>("returnsOut", rows, AtomicsDifferentialKernels.InterlockedUInt32Columns, OutputPolicy.Exact)
                    .In("contendedState", [0x11223344u], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchInterlockedUInt32OverloadsKernel(
                b.Get<uint>("input"),
                b.Get<uint>("uncontendedSlots"),
                b.Get<uint>("returnsOut"),
                b.Get<uint>("contendedState"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var inUInt = b.Get<uint>("input");
                var uncontendedUInt = b.Get<uint>("uncontendedSlots");
                var returnsOut = b.Get<uint>("returnsOut");
                var contendedUInt = b.Get<uint>("contendedState");

                int rows = s.ThreadCount;

                for (int tid = 0; tid < rows; tid++)
                {
                    uint vu = inUInt[tid];

                    uint retExchU32 = uncontendedUInt[tid * 2 + 0];
                    uncontendedUInt[tid * 2 + 0] = vu + 100u;

                    uint retCasHitU32 = uncontendedUInt[tid * 2 + 1];
                    uncontendedUInt[tid * 2 + 1] = vu + 200u;

                    int o = tid * 2;
                    returnsOut[o + 0] = retExchU32;
                    returnsOut[o + 1] = retCasHitU32;
                }

                contendedUInt[0] = 0x87654321u;
            },
        };

    private static readonly DifferentialCase s_interlockedInt64Overloads =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.InterlockedInt64OverloadsKernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inLong = new long[rows];
                var uncontendedLong = new long[rows * 3];

                for (int i = 0; i < rows; i++)
                {
                    inLong[i] = ((long)rng.NextUInt32() << 16) ^ i;
                    uncontendedLong[i * 3 + 0] = 1000L + i;
                    uncontendedLong[i * 3 + 1] = 2000L + i;
                    uncontendedLong[i * 3 + 2] = 3000L + i;
                }

                return new DiffBuffers()
                    .In("input", inLong, OutputPolicy.Exact, readOnly: true)
                    .In("uncontendedSlots", uncontendedLong, OutputPolicy.Exact)
                    .Out<long>("returnsOut", rows, AtomicsDifferentialKernels.InterlockedInt64Columns, OutputPolicy.Exact)
                    .In("contendedState", [500L, -1234567890L], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchInterlockedInt64OverloadsKernel(
                b.Get<long>("input"),
                b.Get<long>("uncontendedSlots"),
                b.Get<long>("returnsOut"),
                b.Get<long>("contendedState"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var inLong = b.Get<long>("input");
                var uncontendedLong = b.Get<long>("uncontendedSlots");
                var returnsOut = b.Get<long>("returnsOut");
                var contendedLong = b.Get<long>("contendedState");

                int rows = s.ThreadCount;

                for (int tid = 0; tid < rows; tid++)
                {
                    long vl = inLong[tid];

                    uncontendedLong[tid * 3 + 0] += vl;
                    long retAddI64 = uncontendedLong[tid * 3 + 0];

                    long retExchI64 = uncontendedLong[tid * 3 + 1];
                    uncontendedLong[tid * 3 + 1] = vl + 1000L;

                    long retCasHitI64 = uncontendedLong[tid * 3 + 2];
                    uncontendedLong[tid * 3 + 2] = vl + 2000L;

                    contendedLong[0] += vl;

                    int o = tid * 3;
                    returnsOut[o + 0] = retAddI64;
                    returnsOut[o + 1] = retExchI64;
                    returnsOut[o + 2] = retCasHitI64;
                }

                contendedLong[1] = -9876543210L;
            },
        };

    private static readonly DifferentialCase s_interlockedUInt64Overloads =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.InterlockedUInt64OverloadsKernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inULong = new ulong[rows];
                var uncontendedULong = new ulong[rows * 2];

                for (int i = 0; i < rows; i++)
                {
                    inULong[i] = ((ulong)rng.NextUInt32() << 32) | rng.NextUInt32();
                    uncontendedULong[i * 2 + 0] = (ulong)(4000 + i);
                    uncontendedULong[i * 2 + 1] = (ulong)(5000 + i);
                }

                return new DiffBuffers()
                    .In("input", inULong, OutputPolicy.Exact, readOnly: true)
                    .In("uncontendedSlots", uncontendedULong, OutputPolicy.Exact)
                    .Out<ulong>("returnsOut", rows, AtomicsDifferentialKernels.InterlockedUInt64Columns, OutputPolicy.Exact)
                    .In("contendedState", [0x0123456789ABCDEFUL], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchInterlockedUInt64OverloadsKernel(
                b.Get<ulong>("input"),
                b.Get<ulong>("uncontendedSlots"),
                b.Get<ulong>("returnsOut"),
                b.Get<ulong>("contendedState"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var inULong = b.Get<ulong>("input");
                var uncontendedULong = b.Get<ulong>("uncontendedSlots");
                var returnsOut = b.Get<ulong>("returnsOut");
                var contendedULong = b.Get<ulong>("contendedState");

                int rows = s.ThreadCount;

                for (int tid = 0; tid < rows; tid++)
                {
                    ulong vul = inULong[tid];

                    ulong retExchU64 = uncontendedULong[tid * 2 + 0];
                    uncontendedULong[tid * 2 + 0] = vul + 300UL;

                    ulong retCasHitU64 = uncontendedULong[tid * 2 + 1];
                    uncontendedULong[tid * 2 + 1] = vul + 400UL;

                    int o = tid * 2;
                    returnsOut[o + 0] = retExchU64;
                    returnsOut[o + 1] = retCasHitU64;
                }

                contendedULong[0] = 0xFEDCBA9876543210UL;
            },
        };

    private static readonly DifferentialCase s_interlockedReturnExpression =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.InterlockedReturnExpressionKernel)}")
        {
            Shape = LaunchShape.ForRows(32, blockX: 32),
            Inputs = static (_, s) =>
            {
                int rows = s.ThreadCount;
                var state64 = new long[rows * 3];
                var state32 = new int[rows * 2];
                for (int i = 0; i < rows; i++)
                {
                    state64[i * 3 + 0] = -10L;
                    state64[i * 3 + 1] = -64L;
                    state64[i * 3 + 2] = -10L;

                    state32[i * 2 + 0] = int.MaxValue;
                    state32[i * 2 + 1] = 100;
                }

                return new DiffBuffers()
                    .In("state64", state64, OutputPolicy.Exact)
                    .In("state32", state32, OutputPolicy.Ignore)
                    .Out<int>("output", rows, AtomicsDifferentialKernels.InterlockedExpressionColumns, OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchInterlockedReturnExpressionKernel(
                b.Get<long>("state64"),
                b.Get<int>("state32"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var state64 = b.Get<long>("state64");
                var state32 = b.Get<int>("state32");
                var output = b.Get<int>("output");
                int rows = s.ThreadCount;

                for (int tid = 0; tid < rows; tid++)
                {
                    state64[tid * 3 + 0] = -15L;
                    state64[tid * 3 + 1] = 42L;
                    state64[tid * 3 + 2] = 99L;

                    state32[tid * 2 + 0] = int.MinValue;
                    state32[tid * 2 + 1] = 555;

                    int o = tid * 5;
                    output[o + 0] = 1;  // -15L < 0L is true (1)
                    output[o + 1] = -1; // (-64L) >> 62 is -1 (arithmetic shift)
                    output[o + 2] = 1;  // -10L < 0L is true (1)
                    output[o + 3] = 1;  // unchecked(int.MaxValue + 1) == int.MinValue is true (1)
                    output[o + 4] = 555; // CompareExchange(ref slot, comparand: 100, value: 555) updates 100 -> 555
                }
            },
        };

    private static readonly DifferentialCase s_floatExactPowersOfTwoAtomics =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.FloatExactPowersOfTwoAtomicKernel)}")
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
                .In("packets", [new DiffAtomicPacket { Counter = 0, Flags = 0u, Accumulator = 4.0f }], OutputPolicy.Exact),
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
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.FloatTwoStageRoundingBoundKernel)}")
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
                    // Positive normal values in [0.5f, 2.0f) with non-zero low mantissa bits so rounding depends on order
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
                            $"Accumulator[{k}]={acc[k]:R} exceeded Higham bound: |err|={err:E4} > {maxAllowedError:E4} (exact={exactSum:R})."
                        );
                    }
                }

                return PropertyResult.Pass();
            },
        };

    private static readonly DifferentialCase s_floatSpecialValuesContention =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.FloatSpecialValuesContentionKernel)}")
        {
            Shape = LaunchShape.ForRows(32, blockX: 32),
            Inputs = static (_, _) =>
            {
                float subnormal = BitConverter.UInt32BitsToSingle(0x00400000u); // largest-bit subnormal (2^-127)
                float nanPayload = BitConverter.UInt32BitsToSingle(0x7FC01234u); // non-canonical quiet NaN with payload
                return new DiffBuffers()
                    .In("specialInputs", [subnormal, nanPayload, -0.0f, 1.0f, float.NegativeInfinity], OutputPolicy.Exact, readOnly: true)
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
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.BlockLeaderCasSpinlockKernel)}")
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
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.CooperativeCasLockFreeStackKernel)}")
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
                        return PropertyResult.Fail($"Duplicate popped node {node} at thread {i} (ABA or lost CAS update).");
                    }

                    seen[node] = true;
                }

                return PropertyResult.Pass();
            },
        };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_IntegerSameStepAtomics_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integerSameStepAtomics);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerArbitraryAtomicsInt32_MatchesTheReferenceAndConservationProperties() =>
        DifferentialRunner.Run(s_integerArbitraryAtomicsInt32);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerArbitraryAtomicsUInt32_MatchesTheReferenceAndConservationProperties() =>
        DifferentialRunner.Run(s_integerArbitraryAtomicsUInt32);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerArbitraryAtomicsUInt64_MatchesTheReferenceAndConservationProperties() =>
        DifferentialRunner.Run(s_integerArbitraryAtomicsUInt64);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_IntegerSharedMemoryTwoStageAtomicsInt32_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integerSharedMemoryTwoStageAtomicsInt32);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_IntegerSharedMemoryTwoStageAtomicsUInt32_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integerSharedMemoryTwoStageAtomicsUInt32);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_IntegerSharedMemoryTwoStageAtomicsUInt64_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integerSharedMemoryTwoStageAtomicsUInt64);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterlockedInt32Overloads_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedInt32Overloads);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterlockedUInt32Overloads_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedUInt32Overloads);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterlockedInt64Overloads_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedInt64Overloads);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterlockedUInt64Overloads_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedUInt64Overloads);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_InterlockedReturnExpression_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedReturnExpression);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_FloatExactPowersOfTwoAtomics_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_floatExactPowersOfTwoAtomics);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatTwoStageRoundingBound_SatisfiesHighamForwardErrorBoundOnEveryPath() =>
        DifferentialRunner.Run(s_floatTwoStageRoundingBound);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatSpecialValuesContention_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_floatSpecialValuesContention);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BlockLeaderCasSpinlock_ProtectsMultiWordCriticalSectionOnEveryPath() =>
        DifferentialRunner.Run(s_blockLeaderCasSpinlock);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CooperativeCasLockFreeStack_PushesAndPopsBijectionAcrossGridSync() =>
        DifferentialRunner.Run(s_cooperativeCasLockFreeStack);
}
