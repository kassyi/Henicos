#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="System.Threading.Interlocked"/> intrinsics and atomic return expressions.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.AtomicsDifferential)]
public class AtomicsInterlockedDifferentialTests
{
    private static readonly DifferentialCase s_interlockedInt32Overloads =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.InterlockedInt32OverloadsKernel)}")
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
                    .Out<int>(
                        "returnsOut",
                        rows,
                        AtomicsDifferentialKernels.InterlockedInt32Columns,
                        OutputPolicy.Exact)
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
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.InterlockedUInt32OverloadsKernel)}")
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
                    .Out<uint>(
                        "returnsOut",
                        rows,
                        AtomicsDifferentialKernels.InterlockedUInt32Columns,
                        OutputPolicy.Exact)
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
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.InterlockedInt64OverloadsKernel)}")
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
                    .Out<long>(
                        "returnsOut",
                        rows,
                        AtomicsDifferentialKernels.InterlockedInt64Columns,
                        OutputPolicy.Exact)
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
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.InterlockedUInt64OverloadsKernel)}")
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
                    .Out<ulong>(
                        "returnsOut",
                        rows,
                        AtomicsDifferentialKernels.InterlockedUInt64Columns,
                        OutputPolicy.Exact)
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
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.InterlockedReturnExpressionKernel)}")
        {
            Shape = LaunchShape.ForRows(32, blockX: 32),
            Inputs = static (_, s) =>
            {
                int rows = s.ThreadCount;
                var state64 = new long[rows * 5];
                var state32 = new int[rows * 3];
                for (int i = 0; i < rows; i++)
                {
                    state64[i * 5 + 0] = -10L;
                    state64[i * 5 + 1] = -64L;
                    state64[i * 5 + 2] = -10L;
                    state64[i * 5 + 3] = long.MaxValue;
                    state64[i * 5 + 4] = -7L;

                    state32[i * 3 + 0] = int.MaxValue;
                    state32[i * 3 + 1] = 100;
                    state32[i * 3 + 2] = -1;
                }

                return new DiffBuffers()
                    .In("state64", state64, OutputPolicy.Exact)
                    .In("state32", state32, OutputPolicy.Ignore)
                    .Out<int>(
                        "output",
                        rows,
                        AtomicsDifferentialKernels.InterlockedExpressionColumns,
                        OutputPolicy.Exact);
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
                    state64[tid * 5 + 0] = -15L;
                    state64[tid * 5 + 1] = 42L;
                    state64[tid * 5 + 2] = 99L;
                    state64[tid * 5 + 3] = long.MinValue;
                    state64[tid * 5 + 4] = -9876543210L;

                    state32[tid * 3 + 0] = int.MinValue;
                    state32[tid * 3 + 1] = 555;
                    state32[tid * 3 + 2] = int.MaxValue;

                    int o = tid * 8;
                    output[o + 0] = 1;  // -15L < 0L is true (1)
                    output[o + 1] = -1; // (-64L) >> 62 is -1 (arithmetic shift)
                    output[o + 2] = 1;  // -10L < 0L is true (1)
                    output[o + 3] = 1;  // unchecked(int.MaxValue + 1) == int.MinValue is true (1)
                    output[o + 4] = 555; // CompareExchange(ref slot, comparand: 100, value: 555) updates 100 -> 555
                    output[o + 5] = 1;  // unchecked(long.MaxValue + 1L) == long.MinValue is true (1)
                    output[o + 6] = 1;  // unchecked(-1 + int.MinValue) == int.MaxValue is true (1)
                    // CompareExchange(comparand: -7L, value: -9876543210L) returns -7L; -7L < 0L (1)
                    output[o + 7] = 1;
                }
            },
        };

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterlockedInt32Overloads_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedInt32Overloads);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterlockedUInt32Overloads_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedUInt32Overloads);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterlockedInt64Overloads_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedInt64Overloads);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InterlockedUInt64Overloads_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedUInt64Overloads);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_InterlockedReturnExpression_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_interlockedReturnExpression);
}
