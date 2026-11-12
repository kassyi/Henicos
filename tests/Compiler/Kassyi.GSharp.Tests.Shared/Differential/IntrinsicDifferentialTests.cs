#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.IntrinsicDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of the <c>Gpu</c> intrinsics that the CPU dialect implements in its prelude and the managed path
/// in <c>ManagedIntrinsics</c> (#717), and of <c>Gpu.SmId</c> and the clocks (#469). Each value is checked against a
/// reference written with the .NET operations that define it (<see cref="BitOperations"/>, <c>Math.BigMul</c>,
/// 128-bit integers), never with the intrinsic itself.
/// </summary>
public class IntrinsicDifferentialTests
{
    // Every byte index of {y, x} once in order and reversed, the same index everywhere, and nibbles with their high
    // bit set (8-F), which CUDA ignores
    private static readonly uint[] s_selectors =
    [
        0x3210u, 0x7654u, 0x0123u, 0x4567u, 0x0000u, 0x7777u, 0x89ABu, 0xFEDCu, 0x5140u, 0xFFFFu, 0x8888u, 0xFFFF_3210u,
    ];

    private static readonly DifferentialCase s_integer32 =
        new(KernelName(nameof(IntrinsicDifferentialKernels.Integer32IntrinsicsKernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Seeds = [0, 1],
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("a", Values(rng, shape.Rows, s_uint32Boundaries, 0), readOnly: true)
            .In("b", Values(rng, shape.Rows, s_uint32Boundaries, 3), readOnly: true)
            .In("s", Values(rng, shape.Rows, s_selectors, 0), readOnly: true)
            .Out<uint>("output", shape.Rows, IntrinsicDifferentialKernels.Integer32Columns, OutputPolicy.Exact),
        Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchInteger32IntrinsicsKernel(
            b.Get<uint>("a"),
            b.Get<uint>("b"),
            b.Get<uint>("s"),
            b.Get<uint>("output"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var a = b.Get<uint>("a");
            var bs = b.Get<uint>("b");
            var sel = b.Get<uint>("s");
            var output = b.Get<uint>("output");
            for (int i = 0; i < s.Rows; i++)
            {
                int o = i * 5;
                output[o] = (uint)BitOperations.LeadingZeroCount(a[i]);
                output[o + 1] = PermuteBytes(a[i], bs[i], sel[i]);
                output[o + 2] = (uint)Math.Abs((long)a[i] - bs[i]);
                // The 32 bits of |a - b|, a value up to 2^32 - 1
                output[o + 3] = (uint)Math.Abs((long)(int)a[i] - (int)bs[i]);
                output[o + 4] = (uint)(Math.BigMul((int)a[i], (int)bs[i]) >> 32);
            }
        },
    };

    private static readonly DifferentialCase s_integer64 =
        new(KernelName(nameof(IntrinsicDifferentialKernels.Integer64IntrinsicsKernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Seeds = [0, 1],
        Inputs = static (rng, shape) =>
        {
            var a = Values(rng, shape.Rows, s_uint32Boundaries, 0);
            var b = Values(rng, shape.Rows, s_uint32Boundaries, 5);
            var c = Values(rng, shape.Rows, s_uint64Boundaries, 0);
            var d = Values(rng, shape.Rows, s_uint64Boundaries, 7);
            if (rng.IsBoundarySeed)
            {
                // The carry-in alone carries out of the 64-bit sum: 0 * 0 + ulong.MaxValue + 1
                (a[0], b[0], c[0], d[0]) = (0u, 0u, ulong.MaxValue, 1ul);
            }

            return new DiffBuffers()
                .In("a", a, readOnly: true)
                .In("b", b, readOnly: true)
                .In("c", c, readOnly: true)
                .In("d", d, readOnly: true)
                .Out<ulong>("output", shape.Rows, IntrinsicDifferentialKernels.Integer64Columns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchInteger64IntrinsicsKernel(
            b.Get<uint>("a"),
            b.Get<uint>("b"),
            b.Get<ulong>("c"),
            b.Get<ulong>("d"),
            b.Get<ulong>("output"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var a = b.Get<uint>("a");
            var bs = b.Get<uint>("b");
            var c = b.Get<ulong>("c");
            var d = b.Get<ulong>("d");
            var output = b.Get<ulong>("output");
            for (int i = 0; i < s.Rows; i++)
            {
                int o = i * 8;
                output[o] = (ulong)((UInt128)a[i] * bs[i]);
                output[o + 1] = (ulong)(((UInt128)c[i] * d[i]) >> 64);
                output[o + 2] = (ulong)(long)(((Int128)(long)c[i] * (long)d[i]) >> 64);
                output[o + 3] = (ulong)BitOperations.LeadingZeroCount(c[i]);
                // The sums keep their low 64 bits, as PTX mad.wide does
                output[o + 4] = (ulong)((UInt128)a[i] * bs[i] + c[i]);
                output[o + 5] = (ulong)(long)((Int128)(int)a[i] * (int)bs[i] + (long)c[i]);
                var full = (UInt128)a[i] * bs[i] + c[i] + (d[i] & 1ul);
                output[o + 6] = (ulong)full;
                output[o + 7] = (ulong)(full >> 64);
            }
        },
    };

    private static readonly DifferentialCase s_cacheHintAndFence =
        new(KernelName(nameof(IntrinsicDifferentialKernels.CacheHintAndFenceKernel)))
        {
            Shape = LaunchShapes.Linear(Rows, 32),
            Inputs = static (rng, shape) => new DiffBuffers()
                .In("source", DiffValues.Int32(rng, shape.Rows), readOnly: true)
                .Out<int>("output", shape.Rows, IntrinsicDifferentialKernels.CacheHintColumns, OutputPolicy.Exact),
            Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchCacheHintAndFenceKernel(
                b.Get<int>("source"),
                b.Get<int>("output"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX),
            Reference = static (b, s) =>
            {
                var source = b.Get<int>("source");
                var output = b.Get<int>("output");
                for (int i = 0; i < s.Rows; i++)
                {
                    output[i * 2] = source[i];
                    output[i * 2 + 1] = unchecked(source[i] * 3 + 1);
                }
            },
        };

    private static readonly DifferentialCase s_smId = new(KernelName(nameof(IntrinsicDifferentialKernels.SmIdKernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Inputs = static (_, shape) => new DiffBuffers()
            .Out<int>("output", shape.Rows, OutputPolicy.Ignore),
        Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchSmIdKernel(
            b.Get<int>("output"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        // The number of a streaming multiprocessor: never negative, and below 1024 (no GPU has that many). Which one a
        // block runs on is up to the GPU, so the paths are not compared with each other.
        // mutant: src/Compiler/Kassyi.GSharp.Compiler/Resources/Prelude/WarpAndHints.hpp `return 0;` -> `return -1;`
        // input: every thread
        Verify = static (actual, s) =>
        {
            var output = actual.Get<int>("output");
            for (int i = 0; i < s.Rows; i++)
            {
                if ((uint)output[i] >= 1024u)
                {
                    return PropertyResult.Fail($"Gpu.SmId() of thread {i} is {output[i]}, outside [0, 1024).");
                }
            }

            return PropertyResult.Passed;
        },
    };

    private static readonly DifferentialCase s_clock = new(KernelName(nameof(IntrinsicDifferentialKernels.ClockKernel)))
    {
        Shape = LaunchShapes.Linear(Rows, 32),
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("input", Values(rng, shape.Rows, s_uint32Boundaries, 0), readOnly: true)
            .Out<ulong>("output", shape.Rows, IntrinsicDifferentialKernels.ClockColumns, OutputPolicy.Ignore),
        Launch = static (b, s) => IntrinsicDifferentialKernels.LaunchClockKernel(
            b.Get<uint>("input"),
            b.Get<ulong>("output"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        // A thread reads each counter twice; the counter only grows while the launch runs, so the second reading of
        // Clock64 is not below the first, and the 32-bit Clock, which wraps, moves forward by less than 2^31 between
        // them.
        // mutant: src/Compiler/Kassyi.GSharp.Compiler/Resources/Prelude/ThreadAndMemory.hpp `std::chrono::steady_clock`
        // -> a counter that counts down (`0ull - ...count()`)
        // input: every thread
        Verify = static (actual, s) =>
        {
            var output = actual.Get<ulong>("output");
            for (int i = 0; i < s.Rows; i++)
            {
                int o = i * 4;
                if (output[o + 1] < output[o])
                {
                    return PropertyResult.Fail(
                        $"Gpu.Clock64() of thread {i} went back: {output[o]} then {output[o + 1]}.");
                }

                if ((uint)output[o + 3] - (uint)output[o + 2] >= 0x8000_0000u)
                {
                    return PropertyResult.Fail(
                        $"Gpu.Clock() of thread {i} went back: {output[o + 2]} then {output[o + 3]}.");
                }
            }

            return PropertyResult.Passed;
        },
    };

    // Each column agrees with its .NET definition on boundary pairs and random values.
    // mutant: src/Compiler/Kassyi.GSharp.Compiler/Resources/Prelude/MathAndBitOps.hpp `& 7u` -> `& 3u` in __byte_perm
    // input: selector 0x7654 (the bytes of y). `& 15u` is no mutant on x64, where a shift by 64 or more wraps; the
    // selectors with high bits (0x89AB) are checked against CUDA itself
    // mutant: src/Core/Kassyi.GSharp.Core/ManagedIntegerIntrinsics.cs AbsDiff(int, int)
    // -> `checked((int)Math.Abs((long)a - b))`
    // input: a = 0x80000000, b = 0x7FFFFFFF (int.MinValue - int.MaxValue)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Integer32Intrinsics_BoundaryOperands_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integer32);

    // Each column agrees with its .NET definition, including a carry out of the 64-bit sum and a negative product.
    // mutant: src/Compiler/Kassyi.GSharp.Compiler/Resources/Prelude/MathAndBitOps.hpp __umul64hi
    // `(cross1 >> 32) + (cross2 >> 32)` -> `(cross2 >> 32)`
    // input: row 2 of seed 0, c = ulong.MaxValue, d = ulong.MaxValue - 1
    // mutant: src/Core/Kassyi.GSharp.Core/ManagedIntegerIntrinsics.cs MultiplyAddWideWithCarry
    // `carryOut |= result < partial ? 1ul : 0ul;` removed
    // input: row 0 of seed 0, a = b = 0, c = ulong.MaxValue, carry-in 1
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Integer64Intrinsics_BoundaryOperands_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integer64);

    // A cache hint or a fence changes no value.
    // mutant: src/Compiler/Kassyi.GSharp.Compiler/Resources/Prelude/ThreadAndMemory.hpp __stcg `*address = value;`
    // -> `*address = value + 1;`
    // input: every thread
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void CacheHintsAndFences_ThreadOwnElements_KeepTheValuesOnEveryPath() =>
        DifferentialRunner.Run(s_cacheHintAndFence);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void SmId_EveryThread_IsAStreamingMultiprocessorNumberOnEveryPath() =>
        DifferentialRunner.Run(s_smId);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Clocks_TwoReadings_DoNotGoBackOnEveryPath() =>
        DifferentialRunner.Run(s_clock);

    // Byte n of the result is byte (selector >> 4n) & 7 of the eight bytes x0..x3, y0..y3
    private static uint PermuteBytes(uint x, uint y, uint selector)
    {
        Span<byte> bytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(bytes, x);
        BitConverter.TryWriteBytes(bytes[4..], y);
        uint result = 0;
        for (int n = 0; n < 4; n++)
        {
            result |= (uint)bytes[(int)((selector >> (4 * n)) & 7u)] << (8 * n);
        }

        return result;
    }
}
