#nullable enable
using System;
using System.Numerics;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.IntrinsicDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of the bit, carry, asynchronous copy, sleep and device heap intrinsics of <c>Gpu</c>, which the
/// CPU dialect implements in its prelude and the managed path in <c>ManagedIntrinsics</c> (#717). Each value is checked
/// against the PTX definition written bit by bit, or against .NET operations, never against the intrinsic itself.
/// </summary>
public class IntrinsicBitDifferentialTests
{
    // Bit positions and lengths around the ends of 32 and 64 bits, past them, and outside 0-255, which the GPU takes
    // modulo 256 for 32 bits (-1 is 255, 256 is 0, 300 is 44) and as unsigned values for 64 bits. Each table has its
    // own length, so they meet in many pairs
    private static readonly int[] s_bitPositions = [0, 1, 16, 31, 32, 33, 63, 64, 255, 256, -1, 200];

    private static readonly int[] s_bitLengths = [0, 1, 8, 31, 32, 33, 64, 65, 255, 256, -1, 300, 4];

    // Carry-ins and borrow-ins: 0 and 1, any other non-zero low half (which counts as 1), and a non-zero value whose
    // low 32 bits are zero (which counts as 0)
    private static readonly ulong[] s_carryIns = [0ul, 1ul, 2ul, 0xFFFF_FFFFul, 0x1_0000_0000ul, ulong.MaxValue];

    private static readonly long[] s_borrowIns = [0L, 1L, -1L, 0x1_0000_0000L, long.MinValue, 2L, 0x7FFF_FFFFL];

    // Sizes of one byte up to 64. A negative size is left out: the CPU heap returns null for it and the CUDA device
    // heap a block
    private static readonly int[] s_mallocSizes = [1, 2, 3, 4, 7, 8, 16, 31, 32, 33, 63, 64, 5];

    private static readonly DifferentialCase s_bits32 =
        new(KernelName(nameof(IntrinsicDifferentialKernels.Bits32Kernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Seeds = [0, 1],
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("a", Values(rng, shape.Rows, s_uint32Boundaries, 0), readOnly: true)
            .In("b", Values(rng, shape.Rows, s_uint32Boundaries, 4), readOnly: true)
            .In("c", Values(rng, shape.Rows, s_uint32Boundaries, 9), readOnly: true)
            .In("pos", Cycled(shape.Rows, s_bitPositions), readOnly: true)
            .In("len", Cycled(shape.Rows, s_bitLengths), readOnly: true)
            .Out<uint>("output", shape.Rows, IntrinsicDifferentialKernels.Bits32Columns, OutputPolicy.Exact),
        Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchBits32Kernel(
            b.Get<uint>("a"),
            b.Get<uint>("b"),
            b.Get<uint>("c"),
            b.Get<int>("pos"),
            b.Get<int>("len"),
            b.Get<uint>("output"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var a = b.Get<uint>("a");
            var bs = b.Get<uint>("b");
            var c = b.Get<uint>("c");
            var pos = b.Get<int>("pos");
            var len = b.Get<int>("len");
            var output = b.Get<uint>("output");
            for (int i = 0; i < s.Rows; i++)
            {
                int o = i * 15;
                output[o] = (uint)BitOperations.PopCount(a[i]);
                output[o + 1] = (uint)ReverseBits(a[i], 32);
                output[o + 2] = (uint)BitOperations.TrailingZeroCount(a[i]);
                ulong pair = ((ulong)bs[i] << 32) | a[i];
                output[o + 3] = (uint)((pair << (int)(c[i] % 32)) >> 32);
                output[o + 4] = (uint)(pair >> (int)(c[i] % 32));
                output[o + 5] = (uint)ExtractBits(a[i], (uint)pos[i] & 0xFFu, (uint)len[i] & 0xFFu, 32);
                output[o + 6] = (uint)InsertBits(a[i], bs[i], (uint)pos[i] & 0xFFu, (uint)len[i] & 0xFFu, 32);
                output[o + 7] = TruthTable(a[i], bs[i], c[i], 0x96);
                output[o + 8] = TruthTable(a[i], bs[i], c[i], 0xE8);
                output[o + 9] = TruthTable(a[i], bs[i], c[i], 0xCA);
                output[o + 10] = TruthTable(a[i], bs[i], c[i], 0x00);
                output[o + 11] = TruthTable(a[i], bs[i], c[i], 0xFF);
                output[o + 12] = ByteAbsDifferences(a[i], bs[i], c[i]);
                output[o + 13] = (uint)ByteProducts((int)a[i], (int)bs[i], (int)c[i], signed: true);
                output[o + 14] = (uint)ByteProducts((int)a[i], (int)bs[i], (int)c[i], signed: false);
            }
        },
    };

    private static readonly DifferentialCase s_bits64 =
        new(KernelName(nameof(IntrinsicDifferentialKernels.Bits64Kernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Seeds = [0, 1],
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("a", Values(rng, shape.Rows, s_uint64Boundaries, 0), readOnly: true)
            .In("b", Values(rng, shape.Rows, s_uint64Boundaries, 3), readOnly: true)
            .In("pos", Cycled(shape.Rows, s_bitPositions), readOnly: true)
            .In("len", Cycled(shape.Rows, s_bitLengths), readOnly: true)
            .Out<ulong>("output", shape.Rows, IntrinsicDifferentialKernels.Bits64Columns, OutputPolicy.Exact),
        Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchBits64Kernel(
            b.Get<ulong>("a"),
            b.Get<ulong>("b"),
            b.Get<int>("pos"),
            b.Get<int>("len"),
            b.Get<ulong>("output"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var a = b.Get<ulong>("a");
            var bs = b.Get<ulong>("b");
            var pos = b.Get<int>("pos");
            var len = b.Get<int>("len");
            var output = b.Get<ulong>("output");
            for (int i = 0; i < s.Rows; i++)
            {
                int o = i * 6;
                output[o] = (ulong)BitOperations.PopCount(a[i]);
                output[o + 1] = ReverseBits(a[i], 64);
                output[o + 2] = ReverseBits(a[i], 64);
                output[o + 3] = (ulong)BitOperations.TrailingZeroCount(a[i]);
                output[o + 4] = ExtractBits(a[i], (uint)pos[i], (uint)len[i], 64);
                output[o + 5] = InsertBits(a[i], bs[i], (uint)pos[i], (uint)len[i], 64);
            }
        },
    };

    private static readonly DifferentialCase s_carryChain =
        new(KernelName(nameof(IntrinsicDifferentialKernels.CarryChainKernel)))
        {
            Shape = LaunchShapes.Linear(Rows, 32),
            Seeds = [0, 1],
            Inputs = static (rng, shape) => new DiffBuffers()
                .In("a", Values(rng, shape.Rows, s_uint32Boundaries, 0), readOnly: true)
                .In("b", Values(rng, shape.Rows, s_uint32Boundaries, 6), readOnly: true)
                .In("carryIn", Cycled(shape.Rows, s_carryIns), readOnly: true)
                .In("borrowIn", Cycled(shape.Rows, s_borrowIns), readOnly: true)
                .Out<ulong>("output", shape.Rows, IntrinsicDifferentialKernels.CarryColumns, OutputPolicy.Exact),
            Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchCarryChainKernel(
                b.Get<uint>("a"),
                b.Get<uint>("b"),
                b.Get<ulong>("carryIn"),
                b.Get<long>("borrowIn"),
                b.Get<ulong>("output"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX),
            Reference = static (b, s) =>
            {
                var a = b.Get<uint>("a");
                var bs = b.Get<uint>("b");
                var carryIn = b.Get<ulong>("carryIn");
                var borrowIn = b.Get<long>("borrowIn");
                var output = b.Get<ulong>("output");
                for (int i = 0; i < s.Rows; i++)
                {
                    int o = i * 4;
                    // The carry-in counts as 1 when its low 32 bits are not all zero
                    var sum = (UInt128)a[i] + bs[i] + ((carryIn[i] & 0xFFFF_FFFFul) != 0 ? 1u : 0u);
                    output[o] = (ulong)(uint)sum;
                    output[o + 1] = (ulong)(sum >> 32);
                    var difference = (Int128)a[i] - bs[i] - ((borrowIn[i] & 0xFFFF_FFFFL) != 0 ? 1 : 0);
                    output[o + 2] = (ulong)(uint)(difference & 0xFFFF_FFFF);
                    output[o + 3] = difference < 0 ? 1ul : 0ul;
                }
            },
        };

    private static readonly DifferentialCase s_copyAsyncAndSleep =
        new(KernelName(nameof(IntrinsicDifferentialKernels.CopyAsyncAndSleepKernel)))
        {
            Shape = LaunchShapes.Linear(64, 32),
            Inputs = static (rng, shape) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, shape.Rows), readOnly: true)
                .Out<int>("output", shape.Rows, OutputPolicy.Exact),
            Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchCopyAsyncAndSleepKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX),
            // Each block of 32 comes back reversed in two halves: thread t of the block reads element 15 - t of the
            // first group, or 47 - t of the second
            Reference = static (b, s) =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<int>("output");
                for (int i = 0; i < s.Rows; i++)
                {
                    int blockStart = i - i % 32;
                    int tid = i % 32;
                    output[i] = input[blockStart + (tid < 16 ? 15 - tid : 47 - tid)];
                }
            },
        };

    private static readonly DifferentialCase s_malloc =
        new(KernelName(nameof(IntrinsicDifferentialKernels.MallocKernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Inputs = static (_, shape) => new DiffBuffers()
            .In("sizes", Cycled(shape.Rows, s_mallocSizes), readOnly: true)
            .Out<int>("output", shape.Rows, OutputPolicy.Exact),
        Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchMallocKernel(
            b.Get<int>("sizes"),
            b.Get<int>("output"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        // The sum of the bytes (byte)(i + k) for k below the size
        Reference = static (b, s) =>
        {
            var sizes = b.Get<int>("sizes");
            var output = b.Get<int>("output");
            for (int i = 0; i < s.Rows; i++)
            {
                int sum = 0;
                for (int k = 0; k < sizes[i]; k++)
                {
                    sum += (byte)(i + k);
                }

                output[i] = sum;
            }
        },
    };

    // Each column agrees with the PTX definition, written bit by bit, on boundary values, bit positions and lengths
    // outside 0-31 and 0-255, and random values.
    // mutant: src/Compiler/Kassyi.GSharp.Compiler/Resources/Prelude/MathAndBitOps.hpp __gsharp_bfe
    // `(unsigned int)pos & 0xFFu` -> `(unsigned int)pos`
    // input: pos = 256 (0 modulo 256)
    // mutant: src/Core/Kassyi.GSharp.Core/ManagedBitIntrinsics.cs InsertField `len & 0xFF` -> `len`
    // input: len = 256 (0 modulo 256)
    // mutant: src/Core/Kassyi.GSharp.Core/ManagedIntegerIntrinsics.cs DotProduct4(int, int, int)
    // `(sbyte)` -> `(byte)` on b
    // input: b = 0xFFFFFFFF (bytes -1)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Bits32Intrinsics_BoundaryOperands_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_bits32);

    // The 64-bit forms, also with positions and lengths past 63, which the GPU does not take modulo 256 (measured over
    // every pos and len from -70 to 600 on the GPU: modulo 256 for 32 bits, unsigned values for 64 bits).
    // mutant: src/Core/Kassyi.GSharp.Core/ManagedBitIntrinsics.cs ExtractField `start >= bits` -> `start > bits`
    // input: pos = 64 (a shift by 64 is a shift by 0 in C#)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Bits64Intrinsics_BoundaryOperands_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_bits64);

    // The sum, the difference and the carry and borrow out of 32 bits, with carry-ins and borrow-ins whose low half
    // decides.
    // mutant: src/Core/Kassyi.GSharp.Core/ManagedIntegerIntrinsics.cs AddWithCarry
    // `(uint)carry != 0u` -> `carry != 0ul`
    // input: carry-in 0x1_0000_0000 (low half zero)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void CarryChain_CarryAndBorrowIns_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_carryChain);

    // The copies are visible after their wait, and the sleep changes nothing.
    // mutant: src/Core/Kassyi.GSharp.Core/ManagedIntrinsics.cs CopyAsync `sharedDst = globalSrc;` removed
    // input: every thread
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void CopyAsyncAndNanoSleep_TwoGroups_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_copyAsyncAndSleep);

    // A block of each size holds what the thread wrote until it is freed.
    // mutant: src/Core/Kassyi.GSharp.Core/ManagedIntrinsics.cs Malloc `return NativeMemory.Alloc(...)`
    // -> `return null;`
    // input: every size
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Malloc_SmallSizes_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_malloc);

    private static ulong ReverseBits(ulong value, int bits)
    {
        ulong result = 0;
        for (int i = 0; i < bits; i++)
        {
            if (((value >> i) & 1ul) != 0)
            {
                result |= 1ul << (bits - 1 - i);
            }
        }

        return result;
    }

    // PTX bfe.u32 / bfe.u64, bit by bit: d[i] = (i < len && pos + i <= msb) ? a[pos + i] : 0
    private static ulong ExtractBits(ulong a, uint start, uint length, int bits)
    {
        ulong result = 0;
        for (int i = 0; i < bits; i++)
        {
            if (i < length && start + (long)i < bits && ((a >> (int)(start + i)) & 1ul) != 0)
            {
                result |= 1ul << i;
            }
        }

        return result;
    }

    // PTX bfi.b32 / bfi.b64, bit by bit: f = b; f[pos + i] = a[i] for i < len and pos + i <= msb
    private static ulong InsertBits(ulong a, ulong b, uint start, uint length, int bits)
    {
        ulong result = b;
        for (int i = 0; i < length && start + (long)i < bits; i++)
        {
            ulong bit = 1ul << (int)(start + i);
            result = ((a >> i) & 1ul) != 0 ? result | bit : result & ~bit;
        }

        return result;
    }

    // Bit i of the result is bit (a_i, b_i, c_i) of the 8-entry truth table
    private static uint TruthTable(uint a, uint b, uint c, int table)
    {
        uint result = 0;
        for (int i = 0; i < 32; i++)
        {
            int index = (int)(((a >> i) & 1u) << 2 | ((b >> i) & 1u) << 1 | ((c >> i) & 1u));
            if (((table >> index) & 1) != 0)
            {
                result |= 1u << i;
            }
        }

        return result;
    }

    private static uint ByteAbsDifferences(uint a, uint b, uint c)
    {
        Span<byte> left = stackalloc byte[4];
        Span<byte> right = stackalloc byte[4];
        BitConverter.TryWriteBytes(left, a);
        BitConverter.TryWriteBytes(right, b);
        long sum = c;
        for (int k = 0; k < 4; k++)
        {
            sum += Math.Abs(left[k] - right[k]);
        }

        return (uint)sum;
    }

    private static int ByteProducts(int a, int b, int c, bool signed)
    {
        Span<byte> left = stackalloc byte[4];
        Span<byte> right = stackalloc byte[4];
        BitConverter.TryWriteBytes(left, a);
        BitConverter.TryWriteBytes(right, b);
        long sum = c;
        for (int k = 0; k < 4; k++)
        {
            sum += signed ? (long)(sbyte)left[k] * (sbyte)right[k] : (long)left[k] * right[k];
        }

        return (int)sum;
    }
}
