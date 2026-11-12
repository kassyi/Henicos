#nullable enable
using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.ArrayViewDefaultDifferentialKernels;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="ArrayViewDefaultDifferentialKernels"/>: a default <see cref="ArraySegment{T}"/> or
/// <see cref="ImmutableArray{T}"/> has no array, so <c>Slice</c> and <c>CopyTo</c> throw on every path as C# does, and
/// <c>CopyTo</c> checks its destination array for null before its length. Each seed is one case of the receiver and
/// the destination; the oracle decides the exception (or the values when nothing throws).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class ArrayViewDefaultDifferentialTests : TranspilerTestBase
{
    // The native CPU path still runs a thread on after a failed check (#1714 (C++ __gsharp_trap returns and the thread
    // continues)); set to true when it stops the thread, which the assertions on dst[1] then check on that path too
    private const bool NativeStopsFailingThread = false;

    private static readonly int[] s_backing = [7, 1, 2, 3, 9];

    private static string Name(string kernel) => $"{nameof(ArrayViewDefaultDifferentialKernels)}.{kernel}";

    private static ArraySegment<int> Segment() => new(s_backing, 1, 3);

    // Seeds of a kernel whose mode[0] picks the receiver and mode[1] the call
    private static int[] Mode(int receiver, int call = 0) => [receiver, call];

    private static DiffBuffers Buffers(int[] mode) =>
        new DiffBuffers()
            .In("mode", mode, readOnly: true)
            .Out<int>("dst", 2, OutputPolicy.Exact);

    internal static readonly DifferentialCase s_segmentSlice = new(Name(nameof(SegmentSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        // 0, 1: Slice(1, 1) and Slice(1) of a segment; 2, 3: the same of a default segment
        Seeds = [0, 1, 2, 3],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, _) => Buffers(Mode(rng.Seed < 2 ? ReceiverArgument : ReceiverDefault, rng.Seed % 2)),
        Launch = static (b, s) => LaunchSegmentSliceKernel(Segment(), b.Get<int>("mode"), b.Get<int>("dst"),
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static (b, _) => SegmentSliceKernel(Segment(), b.Get<int>("mode"), b.Get<int>("dst")),
    };

    internal static readonly DifferentialCase s_segmentCopyTo = new(Name(nameof(SegmentCopyToKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        // 0: a segment into a long enough array; 1: a default segment; 2: a segment into null; 3: an empty slice into
        // null; 4: a segment into a short array
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, _) =>
        {
            var receiver = rng.Seed switch
            {
                1 => ReceiverDefault,
                3 => ReceiverEmpty,
                _ => ReceiverArgument,
            };
            int[]? target = rng.Seed switch
            {
                2 or 3 => null,
                4 => new int[2],
                _ => new int[4],
            };
            return Buffers(Mode(receiver)).InNullable("target", target, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LaunchSegmentCopyToKernel(Segment(), b.Get<int>("mode"), b.GetNullable<int>("target"),
            b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static (b, _) =>
            SegmentCopyToKernel(Segment(), b.Get<int>("mode"), b.GetNullable<int>("target"), b.Get<int>("dst")),
    };

    internal static readonly DifferentialCase s_segmentCopyToSegment = new(Name(nameof(SegmentCopyToSegmentKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        // 0: a segment into a segment; 1: a default segment; 2: into a default segment
        Seeds = [0, 1, 2],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, _) =>
            Buffers(Mode(rng.Seed == 1 ? ReceiverDefault : ReceiverArgument, rng.Seed == 2 ? 1 : 0))
                .In("destination", new int[5], OutputPolicy.Exact),
        Launch = static (b, s) => LaunchSegmentCopyToSegmentKernel(Segment(),
            new ArraySegment<int>(b.Get<int>("destination"), 1, 4), b.Get<int>("mode"), b.Get<int>("dst"),
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static (b, _) => SegmentCopyToSegmentKernel(Segment(),
            new ArraySegment<int>(b.Get<int>("destination"), 1, 4), b.Get<int>("mode"), b.Get<int>("dst")),
    };

    internal static readonly DifferentialCase s_immutableCopyTo = new(Name(nameof(ImmutableCopyToKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        // 0: an immutable array into a long enough array; 1: a default immutable array; 2: into null; 3: into a short
        // array
        Seeds = [0, 1, 2, 3],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, _) =>
        {
            int[]? target = rng.Seed switch
            {
                2 => null,
                3 => new int[1],
                _ => new int[3],
            };
            return Buffers(Mode(rng.Seed == 1 ? ReceiverDefault : ReceiverArgument))
                .InNullable("target", target, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LaunchImmutableCopyToKernel(ImmutableArray.Create(4, 5, 6), b.Get<int>("mode"),
            b.GetNullable<int>("target"), b.Get<int>("dst"), gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static (b, _) => ImmutableCopyToKernel(ImmutableArray.Create(4, 5, 6), b.Get<int>("mode"),
            b.GetNullable<int>("target"), b.Get<int>("dst")),
    };

    internal static readonly DifferentialCase s_nullableImmutable = new(Name(nameof(NullableImmutableKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        // 0: three elements; 1: null
        Seeds = [0, 1],
        Inputs = static (rng, _) => new DiffBuffers()
            .In("present", new[] { rng.Seed == 0 ? 1 : 0 }, readOnly: true)
            .Out<int>("dst", 2, OutputPolicy.Exact),
        Launch = static (b, s) => LaunchNullableImmutableKernel(Argument(b), b.Get<int>("dst"),
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static (b, _) => NullableImmutableKernel(Argument(b), b.Get<int>("dst")),
    };

    private static ImmutableArray<int>? Argument(DiffBuffers buffers) =>
        buffers.Get<int>("present")[0] == 1 ? ImmutableArray.Create(1, 2, 3) : null;

    [SkippableFact]
    public void Slice_DefaultSegment_ThrowsInvalidOperationOnEveryPath() =>
        DifferentialRunner.Run(s_segmentSlice);

    [SkippableFact]
    public void CopyTo_DefaultSegmentOrNullDestination_ThrowsWhatCSharpThrowsOnEveryPath() =>
        DifferentialRunner.Run(s_segmentCopyTo);

    [SkippableFact]
    public void CopyTo_DefaultSegmentDestination_ThrowsInvalidOperationOnEveryPath() =>
        DifferentialRunner.Run(s_segmentCopyToSegment);

    [SkippableFact]
    public void CopyTo_DefaultImmutableArrayOrNullDestination_ThrowsWhatCSharpThrowsOnEveryPath() =>
        DifferentialRunner.Run(s_immutableCopyTo);

    [SkippableFact]
    public void Launch_NullImmutableArrayArgument_ReadsNoValueOnEveryPath() =>
        DifferentialRunner.Run(s_nullableImmutable);

    // The values C# leaves: dst[0] written before the failing call, dst[1] never written
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Slice_DefaultSegment_StopsTheThreadAtTheCall(bool forceManaged)
    {
        var dst = new int[2];

        AssertStopsWith<InvalidOperationException>(forceManaged, dst,
            () => LaunchSegmentSliceKernel(Segment(), Mode(ReceiverDefault), dst));
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyTo_EmptySegmentToNull_ThrowsArgumentNullBeforeComparingLengths(bool forceManaged)
    {
        var dst = new int[2];

        var exception = AssertStopsWith<ArgumentNullException>(forceManaged, dst,
            () => LaunchSegmentCopyToKernel(Segment(), Mode(ReceiverEmpty), null, dst));
        Assert.Contains("Value cannot be null.", exception.Message, StringComparison.Ordinal);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyTo_DefaultImmutableArray_ThrowsNullReferenceAndCopiesNothing(bool forceManaged)
    {
        var dst = new int[2];
        var target = new[] { -1, -1, -1 };

        AssertStopsWith<NullReferenceException>(forceManaged, dst,
            () => LaunchImmutableCopyToKernel(ImmutableArray.Create(4, 5, 6), Mode(ReceiverDefault), target, dst));
        Assert.Equal([-1, -1, -1], target);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyTo_ImmutableArray_CopiesEveryElement(bool forceManaged)
    {
        var dst = new int[2];
        var target = new int[4];

        Run(forceManaged, () =>
            LaunchImmutableCopyToKernel(ImmutableArray.Create(4, 5, 6), Mode(ReceiverArgument), target, dst));

        Assert.Equal([4, 5, 6, 0], target);
        Assert.Equal([1, 100], dst);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Launch_ImmutableArrayArgument_ReadsTheLengthOfTheValue(bool forceManaged)
    {
        var dst = new int[2];

        Run(forceManaged, () => LaunchNullableImmutableKernel(ImmutableArray.Create(1, 2, 3), dst));

        Assert.Equal([1, 13], dst);
    }

    // An empty optional view is passed as null, as an empty optional array is, on every path (#860 (Array arguments
    // treat null as empty without a marker) decides whether that stays); the launchers no longer throw
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Launch_EmptyImmutableArrayArgument_ReadsAsNullWithoutThrowing(bool forceManaged)
    {
        var dst = new int[2];

        Run(forceManaged, () => LaunchNullableImmutableKernel(ImmutableArray<int>.Empty, dst));

        Assert.Equal([1, 2], dst);
    }

    private TException AssertStopsWith<TException>(bool forceManaged, int[] dst, Action launch)
        where TException : Exception
    {
        var exception = Assert.Throws<TException>(() => Run(forceManaged, launch));
        Assert.Equal(1, dst[0]);
        if (forceManaged || IsCudaDialect || NativeStopsFailingThread)
        {
            Assert.Equal(0, dst[1]);
        }

        return exception;
    }

    // The managed execution path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
