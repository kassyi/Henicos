// ReSharper disable All
using System.Threading;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Unmanaged packet struct used to verify atomic operations targeting struct fields (<c>ref packets[i].Counter</c>,
/// <c>ref packets[i].Flags</c>, <c>ref packets[i].Accumulator</c>).
/// </summary>
public struct DiffAtomicPacket
{
    public int Counter;
    public uint Flags;
    public float Accumulator;
}

/// <summary>
/// Differential test kernels for <see cref="Gpu"/> atomics, <see cref="Interlocked"/> intrinsics, shared-memory atomics,
/// struct-field atomics, CAS spinlocks, and cooperative lock-free data structures.
/// </summary>
public static partial class AtomicsDifferentialKernels
{
    /// <summary>Per-thread old-value multiset columns for <see cref="IntegerSameStepAtomicsKernel"/>.</summary>
    public static readonly string[] SameStepOldColumns =
    [
        "old_add_i32",
        "old_sub_i32",
        "old_add_u32",
        "old_sub_u32",
        "old_inc_u32_mod8",
        "old_dec_u32_mod8",
    ];

    /// <summary>Final global accumulator columns for <see cref="IntegerSameStepAtomicsKernel"/>.</summary>
    public static readonly string[] SameStepFinalIntColumns =
    [
        "final_add_i32",
        "final_sub_i32",
        "final_red_i32",
        "final_bucket0_i32",
        "final_bucket1_i32",
        "final_bucket2_i32",
        "final_bucket3_i32",
    ];

    /// <summary>Final global unsigned accumulator columns for <see cref="IntegerSameStepAtomicsKernel"/>.</summary>
    public static readonly string[] SameStepFinalUIntColumns =
    [
        "final_add_u32",
        "final_sub_u32",
        "final_red_u32",
        "final_inc_u32",
        "final_dec_u32",
        "final_bucket0_u32",
        "final_bucket1_u32",
        "final_bucket2_u32",
        "final_bucket3_u32",
    ];

    /// <summary>Final global 64-bit unsigned accumulator columns for <see cref="IntegerSameStepAtomicsKernel"/>.</summary>
    public static readonly string[] SameStepFinalULongColumns =
    [
        "final_add_u64",
        "final_red_u64",
    ];

    /// <summary>Final global 32-bit signed state columns for <see cref="IntegerArbitraryAtomicsInt32Kernel"/>.</summary>
    public static readonly string[] ArbitraryFinalInt32Columns =
    [
        "add_i32",
        "sub_i32",
        "min_i32",
        "and_i32",
        "xor_i32",
        "cas_winner_i32",
        "helper_add_i32",
        "exch_slot_i32",
    ];

    /// <summary>Final global 32-bit unsigned state columns for <see cref="IntegerArbitraryAtomicsUInt32Kernel"/>.</summary>
    public static readonly string[] ArbitraryFinalUInt32Columns =
    [
        "add_u32",
        "sub_u32",
        "min_u32",
        "max_u32",
        "and_u32",
        "or_u32",
        "xor_u32",
        "cas_winner_u32",
        "helper_min_u32",
        "exch_slot_u32",
    ];

    /// <summary>Final global 64-bit unsigned state columns for <see cref="IntegerArbitraryAtomicsUInt64Kernel"/>.</summary>
    public static readonly string[] ArbitraryFinalUInt64Columns =
    [
        "add_u64",
        "min_u64",
        "max_u64",
        "and_u64",
        "or_u64",
        "xor_u64",
        "cas_winner_u64",
        "helper_xor_u64",
        "exch_slot_u64",
    ];

    /// <summary>Per-thread output columns for <see cref="IntegerSharedMemoryTwoStageAtomicsInt32Kernel"/>.</summary>
    public static readonly string[] SharedTwoStageThreadInt32Columns =
    [
        "s_add_bucket",
        "s_sub_bucket",
        "s_min_bucket",
        "s_and_bucket",
        "s_xor_bucket",
        "s_cas_slot",
        "dyn_add_bucket",
    ];

    /// <summary>Global cross-block summary columns for <see cref="IntegerSharedMemoryTwoStageAtomicsInt32Kernel"/>.</summary>
    public static readonly string[] SharedTwoStageGlobalInt32Columns =
    [
        "global_sum_i32",
        "global_min_i32",
        "global_xor_i32",
        "global_dyn_sum_i32",
    ];

    /// <summary>Per-thread output columns for <see cref="IntegerSharedMemoryTwoStageAtomicsUInt32Kernel"/>.</summary>
    public static readonly string[] SharedTwoStageThreadUInt32Columns =
    [
        "s_max_bucket_u32",
        "s_or_bucket_u32",
    ];

    /// <summary>Global cross-block summary columns for <see cref="IntegerSharedMemoryTwoStageAtomicsUInt32Kernel"/>.</summary>
    public static readonly string[] SharedTwoStageGlobalUInt32Columns =
    [
        "global_max_u32",
    ];

    /// <summary>Per-thread output columns for <see cref="IntegerSharedMemoryTwoStageAtomicsUInt64Kernel"/>.</summary>
    public static readonly string[] SharedTwoStageThreadUInt64Columns =
    [
        "s_u64_slot",
    ];

    /// <summary>Global cross-block summary columns for <see cref="IntegerSharedMemoryTwoStageAtomicsUInt64Kernel"/>.</summary>
    public static readonly string[] SharedTwoStageGlobalUInt64Columns =
    [
        "global_u64_xor",
    ];

    /// <summary>Per-thread return-value columns for <see cref="InterlockedInt32OverloadsKernel"/>.</summary>
    public static readonly string[] InterlockedInt32Columns =
    [
        "ret_add_i32",
        "ret_exch_i32",
        "ret_cas_hit_i32",
        "ret_cas_miss_i32",
    ];

    /// <summary>Per-thread return-value columns for <see cref="InterlockedUInt32OverloadsKernel"/>.</summary>
    public static readonly string[] InterlockedUInt32Columns =
    [
        "ret_exch_u32",
        "ret_cas_hit_u32",
    ];

    /// <summary>Per-thread return-value columns for <see cref="InterlockedInt64OverloadsKernel"/>.</summary>
    public static readonly string[] InterlockedInt64Columns =
    [
        "ret_add_i64",
        "ret_exch_i64",
        "ret_cas_hit_i64",
    ];

    /// <summary>Per-thread return-value columns for <see cref="InterlockedUInt64OverloadsKernel"/>.</summary>
    public static readonly string[] InterlockedUInt64Columns =
    [
        "ret_exch_u64",
        "ret_cas_hit_u64",
    ];

    /// <summary>Expression-level return-value columns for <see cref="InterlockedReturnExpressionKernel"/>.</summary>
    public static readonly string[] InterlockedExpressionColumns =
    [
        "add64_lt_zero",
        "exch64_sar_sign",
        "cas64_lt_zero",
        "add32_wrap_min",
        "cas32_named_args_reversed",
    ];

    /// <summary>Exact float accumulator columns for <see cref="FloatExactPowersOfTwoAtomicKernel"/>.</summary>
    public static readonly string[] FloatExactSummaryColumns =
    [
        "global_add_0p5",
        "global_red_0p25",
        "static_smem_to_global",
        "dyn_smem_to_global",
    ];

    /// <summary>Special-value float atomic columns for <see cref="FloatSpecialValuesContentionKernel"/>.</summary>
    public static readonly string[] FloatSpecialColumns =
    [
        "global_subnormal_ftz",
        "shared_subnormal_preserved",
        "global_nan_canonical",
        "global_neg_zero_sign",
        "global_pos_inf",
        "global_inf_minus_inf_nan",
    ];

    /// <summary>Critical-section state columns for <see cref="BlockLeaderCasSpinlockKernel"/>.</summary>
    public static readonly string[] SpinlockStateColumns =
    [
        "visited_blocks",
        "accumulated_sum",
        "accumulated_xor",
        "final_lock_state",
    ];

    [GpuDeviceFunction]
    public static int ForwardAtomicAddInt32(ref int target, int delta) =>
        Gpu.AtomicAdd(ref target, delta);

    [GpuDeviceFunction]
    public static uint ForwardAtomicMinUInt32(ref uint target, uint candidate) =>
        Gpu.AtomicMin(ref target, candidate);

    [GpuDeviceFunction]
    public static ulong ForwardAtomicXorUInt64(ref ulong target, ulong mask) =>
        Gpu.AtomicXor(ref target, mask);

    [GpuDeviceFunction]
    public static int ForwardPacketCounterAdd(ref DiffAtomicPacket packet, int delta) =>
        Gpu.AtomicAdd(ref packet.Counter, delta);

    // -------------------------------------------------------------------------
    // Contended same-step integer atomics (Add, Sub, ReductionAdd, Inc, Dec)
    //      across int, uint, ulong on global scalar slots and 4-way bucket slots.
    //      Old values form a deterministic multiset; final values are exact.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void IntegerSameStepAtomicsKernel(
        int[] stateInt,
        uint[] stateUInt,
        ulong[] stateULong,
        int[] oldValuesOut,
        ulong[] oldULongOut)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int bucket = tid & 3;

        int oldAddI32 = Gpu.AtomicAdd(ref stateInt[0], 3);
        int oldSubI32 = Gpu.AtomicSub(ref stateInt[1], 2);
        Gpu.ReductionAdd(ref stateInt[2], 5);
        Gpu.AtomicAdd(ref stateInt[3 + bucket], 7);

        uint oldAddU32 = Gpu.AtomicAdd(ref stateUInt[0], 4u);
        uint oldSubU32 = Gpu.AtomicSub(ref stateUInt[1], 3u);
        Gpu.ReductionAdd(ref stateUInt[2], 6u);
        uint oldIncU32 = Gpu.AtomicInc(ref stateUInt[3], 7u);
        uint oldDecU32 = Gpu.AtomicDec(ref stateUInt[4], 7u);
        Gpu.AtomicSub(ref stateUInt[5 + bucket], 2u);

        ulong oldAddU64 = Gpu.AtomicAdd(ref stateULong[0], 11UL);
        Gpu.ReductionAdd(ref stateULong[1], 13UL);

        int o = tid * 6;
        oldValuesOut[o + 0] = oldAddI32;
        oldValuesOut[o + 1] = oldSubI32;
        oldValuesOut[o + 2] = (int)oldAddU32;
        oldValuesOut[o + 3] = (int)oldSubU32;
        oldValuesOut[o + 4] = (int)oldIncU32;
        oldValuesOut[o + 5] = (int)oldDecU32;
        oldULongOut[tid] = oldAddU64;
    }

    // -------------------------------------------------------------------------
    // Contended arbitrary-input integer atomics across int, uint, ulong.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void IntegerArbitraryAtomicsInt32Kernel(
        int[] inInt,
        int[] stateInt,
        int[] oldValuesInt,
        DiffAtomicPacket[] packets)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int bucket = tid & 3;
        int vInt = inInt[tid];

        // 32-bit signed commutative atomics + single-winner CAS + device helper forwarding
        Gpu.AtomicAdd(ref stateInt[0], vInt);
        Gpu.AtomicSub(ref stateInt[1], vInt);
        Gpu.AtomicMin(ref stateInt[2], vInt);
        Gpu.AtomicAnd(ref stateInt[3], vInt);
        Gpu.AtomicXor(ref stateInt[4], vInt);
        int oldCasInt = Gpu.AtomicCAS(ref stateInt[5], -999999, 100000);
        ForwardAtomicAddInt32(ref stateInt[6], vInt);
        int oldExchInt = Gpu.AtomicExch(ref stateInt[7], vInt);

        oldValuesInt[tid * 2 + 0] = oldExchInt;
        oldValuesInt[tid * 2 + 1] = oldCasInt;

        // Struct-field atomics (direct field ref and via [GpuDeviceFunction] ref struct forwarding)
        ForwardPacketCounterAdd(ref packets[bucket], (vInt & 0xFF) + 1);
        Gpu.AtomicOr(ref packets[bucket].Flags, 1u << (tid & 31));
    }

    [GpuKernel]
    public static void IntegerArbitraryAtomicsUInt32Kernel(
        uint[] inUInt,
        uint[] stateUInt,
        uint[] oldValuesUInt)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        uint vUInt = inUInt[tid];

        // 32-bit unsigned commutative atomics + single-winner CAS + device helper forwarding
        Gpu.AtomicAdd(ref stateUInt[0], vUInt);
        Gpu.AtomicSub(ref stateUInt[1], vUInt);
        Gpu.AtomicMin(ref stateUInt[2], vUInt);
        Gpu.AtomicMax(ref stateUInt[3], vUInt);
        Gpu.AtomicAnd(ref stateUInt[4], vUInt);
        Gpu.AtomicOr(ref stateUInt[5], vUInt);
        Gpu.AtomicXor(ref stateUInt[6], vUInt);
        uint oldCasUInt = Gpu.AtomicCAS(ref stateUInt[7], 0xCAFEBABEu, 0x12345678u);
        ForwardAtomicMinUInt32(ref stateUInt[8], vUInt);
        uint oldExchUInt = Gpu.AtomicExch(ref stateUInt[9], vUInt);

        oldValuesUInt[tid * 2 + 0] = oldExchUInt;
        oldValuesUInt[tid * 2 + 1] = oldCasUInt;
    }

    [GpuKernel]
    public static void IntegerArbitraryAtomicsUInt64Kernel(
        ulong[] inULong,
        ulong[] stateULong,
        ulong[] oldValuesULong)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        ulong vULong = inULong[tid];

        // 64-bit unsigned commutative atomics + single-winner CAS + device helper forwarding
        Gpu.AtomicAdd(ref stateULong[0], vULong);
        Gpu.AtomicMin(ref stateULong[1], vULong);
        Gpu.AtomicMax(ref stateULong[2], vULong);
        Gpu.AtomicAnd(ref stateULong[3], vULong);
        Gpu.AtomicOr(ref stateULong[4], vULong);
        Gpu.AtomicXor(ref stateULong[5], vULong);
        ulong oldCasULong = Gpu.AtomicCAS(ref stateULong[6], 0xDEADBEEF_CAFEBABEUL, 0x01020304_05060708UL);
        ForwardAtomicXorUInt64(ref stateULong[7], vULong);
        ulong oldExchULong = Gpu.AtomicExch(ref stateULong[8], vULong);

        oldValuesULong[tid * 2 + 0] = oldExchULong;
        oldValuesULong[tid * 2 + 1] = oldCasULong;
    }

    // -------------------------------------------------------------------------
    // SharedMemory<T> (static and dynamic) integer atomics + Gpu.SyncThreads()
    //      + block-leader global atomic aggregation across multiple blocks.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void IntegerSharedMemoryTwoStageAtomicsInt32Kernel(
        int[] input,
        int[] threadOutput,
        int[] globalSummary,
        int dynSize)
    {
        int localTid = ThreadIndex.X;
        int gid = BlockIndex.X * BlockDimension.X + localTid;
        int bucket = localTid & 7;

        var sAdd = new SharedMemory<int>(8);
        var sSub = new SharedMemory<int>(8);
        var sMin = new SharedMemory<int>(8);
        var sAnd = new SharedMemory<int>(8);
        var sXor = new SharedMemory<int>(8);
        var sCas = new SharedMemory<int>(4);
        var sDyn = new SharedMemory<int>(dynSize);

        if (localTid < 8)
        {
            sAdd[localTid] = 100;
            sSub[localTid] = 1000;
            sMin[localTid] = int.MaxValue;
            sAnd[localTid] = -1;
            sXor[localTid] = 0;
            sDyn[localTid] = 50;
        }

        if (localTid < 4)
        {
            sCas[localTid] = -1;
        }

        Gpu.SyncThreads();

        int v = (input[gid] & 0x7FFF) - 0x3FFF;

        Gpu.AtomicAdd(ref sAdd[bucket], v);
        Gpu.AtomicSub(ref sSub[bucket], v);
        Gpu.AtomicMin(ref sMin[bucket], v);
        Gpu.AtomicAnd(ref sAnd[bucket], v | 0x101);
        Gpu.AtomicXor(ref sXor[bucket], v);
        Gpu.AtomicCAS(ref sCas[localTid & 3], -1, 777);
        Gpu.AtomicAdd(ref sDyn[bucket], v + localTid);

        Gpu.SyncThreads();

        int o = gid * 7;
        threadOutput[o + 0] = sAdd[bucket];
        threadOutput[o + 1] = sSub[bucket];
        threadOutput[o + 2] = sMin[bucket];
        threadOutput[o + 3] = sAnd[bucket];
        threadOutput[o + 4] = sXor[bucket];
        threadOutput[o + 5] = sCas[localTid & 3];
        threadOutput[o + 6] = sDyn[bucket];

        if (localTid == 0)
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

            Gpu.AtomicAdd(ref globalSummary[0], blockAddTotal);
            Gpu.AtomicMin(ref globalSummary[1], blockMin);
            Gpu.AtomicXor(ref globalSummary[2], blockXor);
            Gpu.AtomicAdd(ref globalSummary[3], blockDynTotal);
        }
    }

    [GpuKernel]
    public static void IntegerSharedMemoryTwoStageAtomicsUInt32Kernel(
        uint[] input,
        uint[] threadOutput,
        uint[] globalSummary)
    {
        int localTid = ThreadIndex.X;
        int gid = BlockIndex.X * BlockDimension.X + localTid;
        int bucket = localTid & 7;

        var sMaxU = new SharedMemory<uint>(8);
        var sOrU = new SharedMemory<uint>(8);

        if (localTid < 8)
        {
            sMaxU[localTid] = 0u;
            sOrU[localTid] = 0u;
        }

        Gpu.SyncThreads();

        uint vu = (uint)(input[gid] & 0xFFFF);

        Gpu.AtomicMax(ref sMaxU[bucket], vu);
        Gpu.AtomicOr(ref sOrU[bucket], vu);

        Gpu.SyncThreads();

        int o = gid * 2;
        threadOutput[o + 0] = sMaxU[bucket];
        threadOutput[o + 1] = sOrU[bucket];

        if (localTid == 0)
        {
            uint blockMaxU = 0u;
            for (int i = 0; i < 8; i++)
            {
                if (sMaxU[i] > blockMaxU)
                {
                    blockMaxU = sMaxU[i];
                }
            }

            Gpu.AtomicMax(ref globalSummary[0], blockMaxU);
        }
    }

    [GpuKernel]
    public static void IntegerSharedMemoryTwoStageAtomicsUInt64Kernel(
        ulong[] input,
        ulong[] threadOutput,
        ulong[] globalSummary)
    {
        int localTid = ThreadIndex.X;
        int gid = BlockIndex.X * BlockDimension.X + localTid;

        var sU64 = new SharedMemory<ulong>(4);

        if (localTid < 4)
        {
            sU64[localTid] = 0UL;
        }

        Gpu.SyncThreads();

        ulong vu64 = input[gid];

        Gpu.AtomicAdd(ref sU64[localTid & 3], vu64);

        Gpu.SyncThreads();

        threadOutput[gid] = sU64[localTid & 3];

        if (localTid == 0)
        {
            ulong blockU64Xor = sU64[0] ^ sU64[1] ^ sU64[2] ^ sU64[3];
            Gpu.AtomicXor(ref globalSummary[0], blockU64Xor);
        }
    }

    // -------------------------------------------------------------------------
    // System.Threading.Interlocked overloads across uncontended per-thread slots,
    //      contended global slots, and static SharedMemory<int>.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void InterlockedInt32OverloadsKernel(
        int[] input,
        int[] uncontendedSlots,
        int[] returnsOut,
        int[] contendedState)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int localTid = ThreadIndex.X;
        int v = (input[tid] & 0x3FFF) - 0x1FFF;

        var sInterlocked = new SharedMemory<int>(4);
        if (localTid < 4)
        {
            sInterlocked[localTid] = 1000;
        }

        Gpu.SyncThreads();

        // 1. Uncontended per-thread slots: verify exact return value (Add returns NEW value, Exchange/CAS return OLD value)
        int retAdd = Interlocked.Add(ref uncontendedSlots[tid * 3 + 0], v);
        int retExch = Interlocked.Exchange(ref uncontendedSlots[tid * 3 + 1], v + 500);
        int expectedCas = uncontendedSlots[tid * 3 + 2];
        int retCasHit = Interlocked.CompareExchange(ref uncontendedSlots[tid * 3 + 2], v + 900, expectedCas);
        int retCasMiss = Interlocked.CompareExchange(ref uncontendedSlots[tid * 3 + 2], -777777, expectedCas);

        // 2. Shared-memory Interlocked.Add and Interlocked.CompareExchange
        Interlocked.Add(ref sInterlocked[localTid & 3], v);
        Interlocked.CompareExchange(ref sInterlocked[0], 1000, -999999);
        Gpu.SyncThreads();

        if (localTid == 0)
        {
            int blockSharedSum = sInterlocked[0] + sInterlocked[1] + sInterlocked[2] + sInterlocked[3];
            Interlocked.Add(ref contendedState[2], blockSharedSum);
        }

        // 3. Contended global Interlocked operations
        Interlocked.Add(ref contendedState[0], v);
        Interlocked.CompareExchange(ref contendedState[1], 424242, -111111);

        int o = tid * 4;
        returnsOut[o + 0] = retAdd;
        returnsOut[o + 1] = retExch;
        returnsOut[o + 2] = retCasHit;
        returnsOut[o + 3] = retCasMiss;
    }

    [GpuKernel]
    public static void InterlockedUInt32OverloadsKernel(
        uint[] input,
        uint[] uncontendedSlots,
        uint[] returnsOut,
        uint[] contendedState)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        uint vu = input[tid];

        uint retExch = Interlocked.Exchange(ref uncontendedSlots[tid * 2 + 0], vu + 100u);
        uint expectedCas = uncontendedSlots[tid * 2 + 1];
        uint retCasHit = Interlocked.CompareExchange(ref uncontendedSlots[tid * 2 + 1], vu + 200u, expectedCas);

        Interlocked.CompareExchange(ref contendedState[0], 0x87654321u, 0x11223344u);

        int o = tid * 2;
        returnsOut[o + 0] = retExch;
        returnsOut[o + 1] = retCasHit;
    }

    [GpuKernel]
    public static void InterlockedInt64OverloadsKernel(
        long[] input,
        long[] uncontendedSlots,
        long[] returnsOut,
        long[] contendedState)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        long vl = input[tid];

        long retAdd = Interlocked.Add(ref uncontendedSlots[tid * 3 + 0], vl);
        long retExch = Interlocked.Exchange(ref uncontendedSlots[tid * 3 + 1], vl + 1000L);
        long expectedCas = uncontendedSlots[tid * 3 + 2];
        long retCasHit = Interlocked.CompareExchange(ref uncontendedSlots[tid * 3 + 2], vl + 2000L, expectedCas);

        Interlocked.Add(ref contendedState[0], vl);
        Interlocked.CompareExchange(ref contendedState[1], -9876543210L, -1234567890L);

        int o = tid * 3;
        returnsOut[o + 0] = retAdd;
        returnsOut[o + 1] = retExch;
        returnsOut[o + 2] = retCasHit;
    }

    [GpuKernel]
    public static void InterlockedUInt64OverloadsKernel(
        ulong[] input,
        ulong[] uncontendedSlots,
        ulong[] returnsOut,
        ulong[] contendedState)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        ulong vul = input[tid];

        ulong retExch = Interlocked.Exchange(ref uncontendedSlots[tid * 2 + 0], vul + 300UL);
        ulong expectedCas = uncontendedSlots[tid * 2 + 1];
        ulong retCasHit = Interlocked.CompareExchange(ref uncontendedSlots[tid * 2 + 1], vul + 400UL, expectedCas);

        Interlocked.CompareExchange(ref contendedState[0], 0xFEDCBA9876543210UL, 0x0123456789ABCDEFUL);

        int o = tid * 2;
        returnsOut[o + 0] = retExch;
        returnsOut[o + 1] = retCasHit;
    }

    // -------------------------------------------------------------------------
    // Direct consumption of Interlocked return values in signed comparisons,
    //      shifts, wraparound, and named-argument calls (#1057, #1058, #1059).
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void InterlockedReturnExpressionKernel(
        long[] state64,
        int[] state32,
        int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        // #1057 probe 1: Interlocked.Add(ref long, long) return value compared directly with < 0L
        int add64LtZero = Interlocked.Add(ref state64[tid * 3 + 0], -5L) < 0L ? 1 : 0;

        // #1057 probe 2: Interlocked.Exchange(ref long, long) return value right-shifted directly (arithmetic vs logical)
        int exch64SarSign = (int)(Interlocked.Exchange(ref state64[tid * 3 + 1], 42L) >> 62);

        // #1057 probe 3: Interlocked.CompareExchange(ref long, long, long) return value compared directly with < 0L
        int cas64LtZero = Interlocked.CompareExchange(ref state64[tid * 3 + 2], 99L, -10L) < 0L ? 1 : 0;

        // #1058 probe: Interlocked.Add(ref int, int) wrapping across int.MaxValue -> int.MinValue
        int add32WrapMin = Interlocked.Add(ref state32[tid * 2 + 0], 1) == int.MinValue ? 1 : 0;

        // #1059 probe: Interlocked.CompareExchange with reversed named arguments (comparand: ..., value: ...)
        Interlocked.CompareExchange(ref state32[tid * 2 + 1], comparand: 100, value: 555);
        int casNamedUpdated = state32[tid * 2 + 1];

        int o = tid * 5;
        output[o + 0] = add64LtZero;
        output[o + 1] = exch64SarSign;
        output[o + 2] = cas64LtZero;
        output[o + 3] = add32WrapMin;
        output[o + 4] = casNamedUpdated;
    }

    // -------------------------------------------------------------------------
    // Exact dyadic (powers-of-two) float atomics (AtomicAdd, ReductionAdd,
    //      AtomicExch) on global memory, static SharedMemory<float>, dynamic
    //      SharedMemory<float>, and DiffAtomicPacket.Accumulator.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void FloatExactPowersOfTwoAtomicKernel(
        float[] summaryFloats,
        float[] exchSlot,
        float[] oldAddMultisetOut,
        float[] oldExchOut,
        DiffAtomicPacket[] packets,
        int dynLen)
    {
        int localTid = ThreadIndex.X;
        int gid = BlockIndex.X * BlockDimension.X + localTid;

        var sStatic = new SharedMemory<float>(4);
        var sDyn = new SharedMemory<float>(dynLen);

        if (localTid < 4)
        {
            sStatic[localTid] = 1.0f;
            sDyn[localTid] = 2.0f;
        }

        Gpu.SyncThreads();

        float oldAdd = Gpu.AtomicAdd(ref summaryFloats[0], 0.5f);
        Gpu.ReductionAdd(ref summaryFloats[1], 0.25f);
        Gpu.AtomicAdd(ref sStatic[localTid & 3], 1.5f);
        Gpu.AtomicAdd(ref sDyn[localTid & 3], -0.25f);
        Gpu.AtomicAdd(ref packets[0].Accumulator, 0.5f);

        float threadExchVal = (gid + 1) * 0.5f;
        float oldExch = Gpu.AtomicExch(ref exchSlot[0], threadExchVal);

        oldAddMultisetOut[gid] = oldAdd;
        oldExchOut[gid] = oldExch;

        Gpu.SyncThreads();

        if (localTid == 0)
        {
            float blockStaticSum = sStatic[0] + sStatic[1] + sStatic[2] + sStatic[3];
            float blockDynSum = sDyn[0] + sDyn[1] + sDyn[2] + sDyn[3];
            Gpu.AtomicAdd(ref summaryFloats[2], blockStaticSum);
            Gpu.AtomicAdd(ref summaryFloats[3], blockDynSum);
        }
    }

    // -------------------------------------------------------------------------
    // Non-power-of-two normal float atomic accumulation in [0.5f, 2.0f)
    //      comparing direct global AtomicAdd and two-stage SharedMemory->global
    //      accumulation against Higham's forward rounding error bound.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void FloatTwoStageRoundingBoundKernel(
        float[] input,
        float[] accumulators)
    {
        int localTid = ThreadIndex.X;
        int gid = BlockIndex.X * BlockDimension.X + localTid;
        float x = input[gid];

        var sBlock = new SharedMemory<float>(4);
        if (localTid < 4)
        {
            sBlock[localTid] = 0.0f;
        }

        Gpu.SyncThreads();

        Gpu.AtomicAdd(ref accumulators[0], x);
        Gpu.ReductionAdd(ref accumulators[1], x);
        Gpu.AtomicAdd(ref sBlock[localTid & 3], x);

        Gpu.SyncThreads();

        if (localTid == 0)
        {
            float blockTotal = (sBlock[0] + sBlock[1]) + (sBlock[2] + sBlock[3]);
            Gpu.AtomicAdd(ref accumulators[2], blockTotal);
        }
    }

    // -------------------------------------------------------------------------
    // Float special values under atomic contention (subnormals on global vs
    //      shared memory, non-canonical NaN payload canonicalization, -0.0f sign
    //      preservation, +Infinity, and (+Inf)+(-Inf) -> NaN) (#868).
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void FloatSpecialValuesContentionKernel(
        float[] specialInputs,
        float[] specialOutputs)
    {
        int localTid = ThreadIndex.X;
        int gid = BlockIndex.X * BlockDimension.X + localTid;

        var sSubnormal = new SharedMemory<float>(1);
        if (localTid == 0)
        {
            sSubnormal[0] = 0.0f;
        }

        Gpu.SyncThreads();

        float subnormalStep = specialInputs[0];
        float nanPayload = specialInputs[1];
        float negZero = specialInputs[2];
        float posOne = specialInputs[3];
        float negInf = specialInputs[4];

        // Column 0: Global subnormal atomic addition (PTX atom.global.add.f32 flushes subnormals to +0.0f; #868)
        Gpu.AtomicAdd(ref specialOutputs[0], subnormalStep);

        // Column 1: Shared-memory subnormal atomic addition (PTX atom.shared.add.f32 preserves subnormals)
        Gpu.AtomicAdd(ref sSubnormal[0], subnormalStep);

        // Column 2: Global NaN payload atomic addition (PTX canonicalizes NaN to 0x7FFFFFFF; #868)
        if (gid == 0)
        {
            Gpu.AtomicAdd(ref specialOutputs[2], nanPayload);
        }

        // Column 3: -0.0f + -0.0f preserves negative zero sign bit (0x80000000)
        Gpu.AtomicAdd(ref specialOutputs[3], negZero);

        // Column 4: +Infinity + 1.0f remains +Infinity
        Gpu.AtomicAdd(ref specialOutputs[4], posOne);

        // Column 5: +Infinity + (-Infinity) produces NaN
        if (gid == 0)
        {
            Gpu.AtomicAdd(ref specialOutputs[5], negInf);
        }

        Gpu.SyncThreads();

        if (localTid == 0 && BlockIndex.X == 0)
        {
            specialOutputs[1] = sSubnormal[0];
        }
    }

    // -------------------------------------------------------------------------
    // Multi-block leader-only CAS spinlock protecting a multi-word
    //      non-atomic critical section + AtomicExch unlock.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void BlockLeaderCasSpinlockKernel(
        int[] blockPayloads,
        int[] lockSlot,
        int[] sharedState)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int blockId = BlockIndex.X;
        int payload = blockPayloads[blockId];

        while (Gpu.AtomicCAS(ref lockSlot[0], 0, 1) != 0)
        {
        }

        int oldCount = sharedState[0];
        int oldSum = sharedState[1];
        int oldXor = sharedState[2];

        sharedState[0] = oldCount + 1;
        sharedState[1] = oldSum + payload;
        sharedState[2] = oldXor ^ (payload * 3 + 0x5A5A);

        Gpu.AtomicExch(ref lockSlot[0], 0);
    }

    // -------------------------------------------------------------------------
    // Cooperative all-threads AtomicCAS Treiber lock-free stack push ->
    //      Gpu.SyncGrid() -> lock-free stack pop.
    // -------------------------------------------------------------------------
    [GpuKernel(Cooperative = true)]
    public static void CooperativeCasLockFreeStackKernel(
        int[] head,
        int[] next,
        int[] popped)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        int oldHead;
        do
        {
            oldHead = head[0];
            next[tid] = oldHead;
        }
        while (Gpu.AtomicCAS(ref head[0], oldHead, tid) != oldHead);

        Gpu.SyncGrid();

        int curHead;
        int nextHead;
        do
        {
            curHead = head[0];
            nextHead = curHead >= 0 ? next[curHead] : -1;
        }
        while (Gpu.AtomicCAS(ref head[0], curHead, nextHead) != curHead);

        popped[tid] = curHead;
    }
}
