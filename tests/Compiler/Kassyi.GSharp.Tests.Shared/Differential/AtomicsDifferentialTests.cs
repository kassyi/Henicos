#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="Gpu"/> atomics, <see cref="System.Threading.Interlocked"/> intrinsics,
/// shared-memory atomics, struct-field atomics, CAS spinlocks, and cooperative lock-free stacks (Lane D4 Stages 1–2).
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

    private static readonly DifferentialCase s_integerArbitraryAtomics =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.IntegerArbitraryAtomicsKernel)}")
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
                var inUInt = new uint[rows];
                var inULong = new ulong[rows];
                for (int i = 0; i < rows; i++)
                {
                    inUInt[i] = rng.IsBoundarySeed
                        ? unchecked((uint)inInt[i])
                        : rng.NextUInt32();
                    inULong[i] = rng.IsBoundarySeed
                        ? ((ulong)inUInt[i] << 32) ^ (uint)i
                        : ((ulong)rng.NextUInt32() << 32) | rng.NextUInt32();
                }

                var packets = new DiffAtomicPacket[4];
                for (int i = 0; i < 4; i++)
                {
                    packets[i] = new DiffAtomicPacket { Counter = 10 * (i + 1), Flags = 0u, Accumulator = 0f };
                }

                return new DiffBuffers()
                    .In("inInt", inInt, OutputPolicy.Exact, readOnly: true)
                    .In("inUInt", inUInt, OutputPolicy.Exact, readOnly: true)
                    .In("inULong", inULong, OutputPolicy.Exact, readOnly: true)
                    .In("stateInt", [50, 500, int.MaxValue, -1, 0x12345678, -999999, 25], OutputPolicy.Exact)
                    .In("stateUInt", [100u, 1000u, uint.MaxValue, 0u, 0xFFFFFFFFu, 0u, 0xA5A5A5A5u, 0xCAFEBABEu, uint.MaxValue], OutputPolicy.Exact)
                    .In("stateULong", [200UL, ulong.MaxValue, 0UL, ulong.MaxValue, 0UL, 0x0F0F0F0F_F0F0F0F0UL, 0xDEADBEEF_CAFEBABEUL, 0UL], OutputPolicy.Exact)
                    .In("exchSlotInt", [777777], OutputPolicy.Ignore)
                    .In("exchSlotUInt", [0x88888888u], OutputPolicy.Ignore)
                    .In("exchSlotULong", [0x99999999_AAAAAAAAUL], OutputPolicy.Ignore)
                    .Out<int>("oldExchAndCasInt", rows * 2, OutputPolicy.Ignore)
                    .Out<uint>("oldExchAndCasUInt", rows * 2, OutputPolicy.Ignore)
                    .Out<ulong>("oldExchAndCasULong", rows * 2, OutputPolicy.Ignore)
                    .In("packets", packets, OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerArbitraryAtomicsKernel(
                b.Get<int>("inInt"),
                b.Get<uint>("inUInt"),
                b.Get<ulong>("inULong"),
                b.Get<int>("stateInt"),
                b.Get<uint>("stateUInt"),
                b.Get<ulong>("stateULong"),
                b.Get<int>("exchSlotInt"),
                b.Get<uint>("exchSlotUInt"),
                b.Get<ulong>("exchSlotULong"),
                b.Get<int>("oldExchAndCasInt"),
                b.Get<uint>("oldExchAndCasUInt"),
                b.Get<ulong>("oldExchAndCasULong"),
                b.Get<DiffAtomicPacket>("packets"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var inInt = b.Get<int>("inInt");
                var inUInt = b.Get<uint>("inUInt");
                var inULong = b.Get<ulong>("inULong");
                var stateInt = b.Get<int>("stateInt");
                var stateUInt = b.Get<uint>("stateUInt");
                var stateULong = b.Get<ulong>("stateULong");
                var packets = b.Get<DiffAtomicPacket>("packets");
                int rows = s.ThreadCount;

                for (int tid = 0; tid < rows; tid++)
                {
                    int bucket = tid & 3;
                    int vInt = inInt[tid];
                    uint vUInt = inUInt[tid];
                    ulong vULong = inULong[tid];

                    stateInt[0] = unchecked(stateInt[0] + vInt);
                    stateInt[1] = unchecked(stateInt[1] - vInt);
                    if (vInt < stateInt[2])
                    {
                        stateInt[2] = vInt;
                    }

                    stateInt[3] &= vInt;
                    stateInt[4] ^= vInt;
                    stateInt[5] = 100000;
                    stateInt[6] = unchecked(stateInt[6] + vInt);

                    stateUInt[0] = unchecked(stateUInt[0] + vUInt);
                    stateUInt[1] = unchecked(stateUInt[1] - vUInt);
                    if (vUInt < stateUInt[2])
                    {
                        stateUInt[2] = vUInt;
                    }

                    if (vUInt > stateUInt[3])
                    {
                        stateUInt[3] = vUInt;
                    }

                    stateUInt[4] &= vUInt;
                    stateUInt[5] |= vUInt;
                    stateUInt[6] ^= vUInt;
                    stateUInt[7] = 0x12345678u;
                    if (vUInt < stateUInt[8])
                    {
                        stateUInt[8] = vUInt;
                    }

                    stateULong[0] = unchecked(stateULong[0] + vULong);
                    if (vULong < stateULong[1])
                    {
                        stateULong[1] = vULong;
                    }

                    if (vULong > stateULong[2])
                    {
                        stateULong[2] = vULong;
                    }

                    stateULong[3] &= vULong;
                    stateULong[4] |= vULong;
                    stateULong[5] ^= vULong;
                    stateULong[6] = 0x01020304_05060708UL;
                    stateULong[7] ^= vULong;

                    packets[bucket].Counter += (vInt & 0xFF) + 1;
                    packets[bucket].Flags |= 1u << (tid & 31);
                }
            },
            VerifyAgainstInitial = static (initial, actual, s) =>
            {
                int rows = s.ThreadCount;

                // 1. Verify AtomicExch multiset conservation: {finalSlot} U {oldExch_i} == {initialSlot} U {input_i}
                var inInt = initial.Get<int>("inInt");
                var oldInt = actual.Get<int>("oldExchAndCasInt");
                var expectedIntSet = new int[rows + 1];
                var actualIntSet = new int[rows + 1];
                expectedIntSet[0] = initial.Get<int>("exchSlotInt")[0];
                actualIntSet[0] = actual.Get<int>("exchSlotInt")[0];
                int casIntWinners = 0;
                for (int i = 0; i < rows; i++)
                {
                    expectedIntSet[i + 1] = inInt[i];
                    actualIntSet[i + 1] = oldInt[i * 2 + 0];
                    int casOld = oldInt[i * 2 + 1];
                    if (casOld == -999999)
                    {
                        casIntWinners++;
                    }
                    else if (casOld != 100000)
                    {
                        return PropertyResult.Fail($"Unexpected int CAS old value {casOld} at thread {i}.");
                    }
                }

                Array.Sort(expectedIntSet);
                Array.Sort(actualIntSet);
                if (!expectedIntSet.AsSpan().SequenceEqual(actualIntSet))
                {
                    return PropertyResult.Fail("AtomicExch(int) conservation multiset mismatch.");
                }

                if (casIntWinners != 1)
                {
                    return PropertyResult.Fail($"Expected exactly 1 int CAS winner, observed {casIntWinners}.");
                }

                var inUInt = initial.Get<uint>("inUInt");
                var oldUInt = actual.Get<uint>("oldExchAndCasUInt");
                var expectedUIntSet = new uint[rows + 1];
                var actualUIntSet = new uint[rows + 1];
                expectedUIntSet[0] = initial.Get<uint>("exchSlotUInt")[0];
                actualUIntSet[0] = actual.Get<uint>("exchSlotUInt")[0];
                int casUIntWinners = 0;
                for (int i = 0; i < rows; i++)
                {
                    expectedUIntSet[i + 1] = inUInt[i];
                    actualUIntSet[i + 1] = oldUInt[i * 2 + 0];
                    uint casOld = oldUInt[i * 2 + 1];
                    if (casOld == 0xCAFEBABEu)
                    {
                        casUIntWinners++;
                    }
                    else if (casOld != 0x12345678u)
                    {
                        return PropertyResult.Fail($"Unexpected uint CAS old value 0x{casOld:X8} at thread {i}.");
                    }
                }

                Array.Sort(expectedUIntSet);
                Array.Sort(actualUIntSet);
                if (!expectedUIntSet.AsSpan().SequenceEqual(actualUIntSet))
                {
                    return PropertyResult.Fail("AtomicExch(uint) conservation multiset mismatch.");
                }

                if (casUIntWinners != 1)
                {
                    return PropertyResult.Fail($"Expected exactly 1 uint CAS winner, observed {casUIntWinners}.");
                }

                var inULong = initial.Get<ulong>("inULong");
                var oldULong = actual.Get<ulong>("oldExchAndCasULong");
                var expectedULongSet = new ulong[rows + 1];
                var actualULongSet = new ulong[rows + 1];
                expectedULongSet[0] = initial.Get<ulong>("exchSlotULong")[0];
                actualULongSet[0] = actual.Get<ulong>("exchSlotULong")[0];
                int casULongWinners = 0;
                for (int i = 0; i < rows; i++)
                {
                    expectedULongSet[i + 1] = inULong[i];
                    actualULongSet[i + 1] = oldULong[i * 2 + 0];
                    ulong casOld = oldULong[i * 2 + 1];
                    if (casOld == 0xDEADBEEF_CAFEBABEUL)
                    {
                        casULongWinners++;
                    }
                    else if (casOld != 0x01020304_05060708UL)
                    {
                        return PropertyResult.Fail($"Unexpected ulong CAS old value 0x{casOld:X16} at thread {i}.");
                    }
                }

                Array.Sort(expectedULongSet);
                Array.Sort(actualULongSet);
                if (!expectedULongSet.AsSpan().SequenceEqual(actualULongSet))
                {
                    return PropertyResult.Fail("AtomicExch(ulong) conservation multiset mismatch.");
                }

                if (casULongWinners != 1)
                {
                    return PropertyResult.Fail($"Expected exactly 1 ulong CAS winner, observed {casULongWinners}.");
                }

                return PropertyResult.Pass();
            },
        };

    private static readonly DifferentialCase s_integerSharedMemoryTwoStageAtomics =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.IntegerSharedMemoryTwoStageAtomicsKernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(128, blockX: 64),
            ],
            Inputs = static (rng, s) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, s.ThreadCount), OutputPolicy.Exact, readOnly: true)
                .Out<int>("threadOutput", s.ThreadCount, AtomicsDifferentialKernels.SharedTwoStageThreadColumns, OutputPolicy.Exact)
                .In("globalSummary", [0, int.MaxValue, 0, 0, 0, 0], OutputPolicy.Exact)
                .In("globalMaxU", [0u], OutputPolicy.Exact),
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerSharedMemoryTwoStageAtomicsKernel(
                b.Get<int>("input"),
                b.Get<int>("threadOutput"),
                b.Get<int>("globalSummary"),
                b.Get<uint>("globalMaxU"),
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
                var globalMaxU = b.Get<uint>("globalMaxU");
                int blockSize = (int)s.BlockX;
                int totalThreads = s.ThreadCount;

                Span<int> sAdd = stackalloc int[8];
                Span<int> sSub = stackalloc int[8];
                Span<int> sMin = stackalloc int[8];
                Span<uint> sMaxU = stackalloc uint[8];
                Span<int> sAnd = stackalloc int[8];
                Span<uint> sOrU = stackalloc uint[8];
                Span<int> sXor = stackalloc int[8];
                Span<int> sCas = stackalloc int[4];
                Span<ulong> sU64 = stackalloc ulong[4];
                Span<int> sDyn = stackalloc int[8];

                for (int blockStart = 0; blockStart < totalThreads; blockStart += blockSize)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        sAdd[i] = 100;
                        sSub[i] = 1000;
                        sMin[i] = int.MaxValue;
                        sMaxU[i] = 0u;
                        sAnd[i] = -1;
                        sOrU[i] = 0u;
                        sXor[i] = 0;
                        sDyn[i] = 50;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        sCas[i] = -1;
                        sU64[i] = 0UL;
                    }

                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = blockStart + localTid;
                        int bucket = localTid & 7;
                        int v = (input[gid] & 0x7FFF) - 0x3FFF;
                        uint vu = (uint)(input[gid] & 0xFFFF);
                        ulong vu64 = ((ulong)vu << 16) ^ (ulong)(uint)localTid;

                        sAdd[bucket] += v;
                        sSub[bucket] -= v;
                        if (v < sMin[bucket])
                        {
                            sMin[bucket] = v;
                        }

                        if (vu > sMaxU[bucket])
                        {
                            sMaxU[bucket] = vu;
                        }

                        sAnd[bucket] &= (v | 0x101);
                        sOrU[bucket] |= vu;
                        sXor[bucket] ^= v;
                        sCas[localTid & 3] = 777;
                        sU64[localTid & 3] += vu64;
                        sDyn[bucket] += v + localTid;
                    }

                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = blockStart + localTid;
                        int bucket = localTid & 7;
                        int o = gid * 9;
                        threadOutput[o + 0] = sAdd[bucket];
                        threadOutput[o + 1] = sSub[bucket];
                        threadOutput[o + 2] = sMin[bucket];
                        threadOutput[o + 3] = (int)sMaxU[bucket];
                        threadOutput[o + 4] = sAnd[bucket];
                        threadOutput[o + 5] = (int)sOrU[bucket];
                        threadOutput[o + 6] = sXor[bucket];
                        threadOutput[o + 7] = sCas[localTid & 3];
                        threadOutput[o + 8] = sDyn[bucket];
                    }

                    int blockAddTotal = 0;
                    int blockMin = int.MaxValue;
                    uint blockMaxU = 0u;
                    int blockXor = 0;
                    int blockDynTotal = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        blockAddTotal += sAdd[i];
                        if (sMin[i] < blockMin)
                        {
                            blockMin = sMin[i];
                        }

                        if (sMaxU[i] > blockMaxU)
                        {
                            blockMaxU = sMaxU[i];
                        }

                        blockXor ^= sXor[i];
                        blockDynTotal += sDyn[i];
                    }

                    ulong blockU64Xor = sU64[0] ^ sU64[1] ^ sU64[2] ^ sU64[3];

                    globalSummary[0] += blockAddTotal;
                    if (blockMin < globalSummary[1])
                    {
                        globalSummary[1] = blockMin;
                    }

                    if (blockMaxU > globalMaxU[0])
                    {
                        globalMaxU[0] = blockMaxU;
                    }

                    globalSummary[3] ^= blockXor;
                    globalSummary[4] += blockDynTotal;
                    globalSummary[5] ^= (int)blockU64Xor ^ (int)(blockU64Xor >> 32);
                }
            },
        };

    private static readonly DifferentialCase s_interlockedAllOverloads =
        new($"{nameof(AtomicsDifferentialKernels)}.{nameof(AtomicsDifferentialKernels.InterlockedAllOverloadsKernel)}")
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
                var uncontendedUInt = new uint[rows * 2];
                var uncontendedLong = new long[rows * 3];
                var uncontendedULong = new ulong[rows * 2];

                for (int i = 0; i < rows; i++)
                {
                    uncontendedInt[i * 3 + 0] = 100 + i;
                    uncontendedInt[i * 3 + 1] = 200 + i;
                    uncontendedInt[i * 3 + 2] = 300 + i;

                    uncontendedUInt[i * 2 + 0] = (uint)(400 + i);
                    uncontendedUInt[i * 2 + 1] = (uint)(500 + i);

                    uncontendedLong[i * 3 + 0] = 1000L + i;
                    uncontendedLong[i * 3 + 1] = 2000L + i;
                    uncontendedLong[i * 3 + 2] = 3000L + i;

                    uncontendedULong[i * 2 + 0] = (ulong)(4000 + i);
                    uncontendedULong[i * 2 + 1] = (ulong)(5000 + i);
                }

                return new DiffBuffers()
                    .In("inInt", inInt, OutputPolicy.Exact, readOnly: true)
                    .In("uncontendedIntSlots", uncontendedInt, OutputPolicy.Exact)
                    .In("uncontendedUIntSlots", uncontendedUInt, OutputPolicy.Exact)
                    .In("uncontendedLongSlots", uncontendedLong, OutputPolicy.Exact)
                    .In("uncontendedULongSlots", uncontendedULong, OutputPolicy.Exact)
                    .Out<int>("uncontendedReturnsOut", rows, AtomicsDifferentialKernels.InterlockedUncontendedColumns, OutputPolicy.Exact)
                    .In("contendedIntState", [50, -111111, 0], OutputPolicy.Exact)
                    .In("contendedUIntState", [0x11223344u], OutputPolicy.Exact)
                    .In("contendedLongState", [500L, -1234567890L], OutputPolicy.Exact)
                    .In("contendedULongState", [0x0123456789ABCDEFUL], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchInterlockedAllOverloadsKernel(
                b.Get<int>("inInt"),
                b.Get<int>("uncontendedIntSlots"),
                b.Get<uint>("uncontendedUIntSlots"),
                b.Get<long>("uncontendedLongSlots"),
                b.Get<ulong>("uncontendedULongSlots"),
                b.Get<int>("uncontendedReturnsOut"),
                b.Get<int>("contendedIntState"),
                b.Get<uint>("contendedUIntState"),
                b.Get<long>("contendedLongState"),
                b.Get<ulong>("contendedULongState"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var inInt = b.Get<int>("inInt");
                var uncontendedInt = b.Get<int>("uncontendedIntSlots");
                var uncontendedUInt = b.Get<uint>("uncontendedUIntSlots");
                var uncontendedLong = b.Get<long>("uncontendedLongSlots");
                var uncontendedULong = b.Get<ulong>("uncontendedULongSlots");
                var uncontendedReturns = b.Get<int>("uncontendedReturnsOut");
                var contendedInt = b.Get<int>("contendedIntState");
                var contendedUInt = b.Get<uint>("contendedUIntState");
                var contendedLong = b.Get<long>("contendedLongState");
                var contendedULong = b.Get<ulong>("contendedULongState");

                int rows = s.ThreadCount;
                int blockSize = (int)s.BlockX;
                int blocks = rows / blockSize;

                for (int tid = 0; tid < rows; tid++)
                {
                    int v = (inInt[tid] & 0x3FFF) - 0x1FFF;
                    uint vu = (uint)(v & 0xFFFF) + 1u;
                    long vl = (long)v * 17L;
                    ulong vul = (ulong)vu * 33UL;

                    uncontendedInt[tid * 3 + 0] += v;
                    int retAddI32 = uncontendedInt[tid * 3 + 0];

                    int retExchI32 = uncontendedInt[tid * 3 + 1];
                    uncontendedInt[tid * 3 + 1] = v + 500;

                    int retCasHitI32 = uncontendedInt[tid * 3 + 2];
                    uncontendedInt[tid * 3 + 2] = v + 900;
                    int retCasMissI32 = uncontendedInt[tid * 3 + 2];

                    uint retExchU32 = uncontendedUInt[tid * 2 + 0];
                    uncontendedUInt[tid * 2 + 0] = vu + 100u;

                    uint retCasHitU32 = uncontendedUInt[tid * 2 + 1];
                    uncontendedUInt[tid * 2 + 1] = vu + 200u;

                    uncontendedLong[tid * 3 + 0] += vl;
                    long retAddI64 = uncontendedLong[tid * 3 + 0];

                    long retExchI64 = uncontendedLong[tid * 3 + 1];
                    uncontendedLong[tid * 3 + 1] = vl + 1000L;

                    long retCasHitI64 = uncontendedLong[tid * 3 + 2];
                    uncontendedLong[tid * 3 + 2] = vl + 2000L;

                    ulong retExchU64 = uncontendedULong[tid * 2 + 0];
                    uncontendedULong[tid * 2 + 0] = vul + 300UL;

                    ulong retCasHitU64 = uncontendedULong[tid * 2 + 1];
                    uncontendedULong[tid * 2 + 1] = vul + 400UL;

                    contendedInt[0] += v;
                    contendedInt[2] += v;
                    contendedLong[0] += vl;

                    int o = tid * 11;
                    uncontendedReturns[o + 0] = retAddI32;
                    uncontendedReturns[o + 1] = retExchI32;
                    uncontendedReturns[o + 2] = retCasHitI32;
                    uncontendedReturns[o + 3] = retCasMissI32;
                    uncontendedReturns[o + 4] = (int)retExchU32;
                    uncontendedReturns[o + 5] = (int)retCasHitU32;
                    uncontendedReturns[o + 6] = (int)retAddI64;
                    uncontendedReturns[o + 7] = (int)retExchI64;
                    uncontendedReturns[o + 8] = (int)retCasHitI64;
                    uncontendedReturns[o + 9] = (int)retExchU64;
                    uncontendedReturns[o + 10] = (int)retCasHitU64;
                }

                contendedInt[1] = 424242;
                contendedInt[2] += blocks * 4 * 1000;
                contendedUInt[0] = 0x87654321u;
                contendedLong[1] = -9876543210L;
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
    public void Test_IntegerArbitraryAtomics_MatchesTheReferenceAndConservationProperties() =>
        DifferentialRunner.Run(s_integerArbitraryAtomics);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_IntegerSharedMemoryTwoStageAtomics_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integerSharedMemoryTwoStageAtomics);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterlockedAllOverloads_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedAllOverloads);

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
