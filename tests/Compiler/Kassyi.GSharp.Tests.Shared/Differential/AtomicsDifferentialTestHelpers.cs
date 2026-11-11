#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Shared test helpers, simulation oracles, and property check methods for atomics differential tests.
/// </summary>
internal static class AtomicsDifferentialTestHelpers
{
    /// <summary>
    /// Verifies multiset conservation and exact single-winner semantics for atomic exchange and CAS operations.
    /// </summary>
    internal static PropertyResult CheckExchangeAndCas<T>(
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

    /// <summary>
    /// Sequential CPU reference oracle for two-stage shared-memory int32 atomics.
    /// </summary>
    internal static void SimulateSharedMemoryTwoStageInt32(DiffBuffers b, LaunchShape s)
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

            ReduceSharedMemoryTwoStageInt32Block(sAdd, sMin, sXor, sDyn, globalSummary);
        }
    }

    private static void ReduceSharedMemoryTwoStageInt32Block(
        ReadOnlySpan<int> sAdd,
        ReadOnlySpan<int> sMin,
        ReadOnlySpan<int> sXor,
        ReadOnlySpan<int> sDyn,
        int[] globalSummary)
    {
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

    /// <summary>
    /// Sequential CPU reference oracle for two-stage shared-memory uint32 atomics.
    /// </summary>
    internal static void SimulateSharedMemoryTwoStageUInt32(DiffBuffers b, LaunchShape s)
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
    }

    /// <summary>
    /// Sequential CPU reference oracle for two-stage shared-memory uint64 atomics.
    /// </summary>
    internal static void SimulateSharedMemoryTwoStageUInt64(DiffBuffers b, LaunchShape s)
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
    }
}
