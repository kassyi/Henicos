#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A failed C# check in the body of a <c>using</c> or in its <c>Dispose</c> disposes as C# runs <c>Dispose</c> in a
/// <c>finally</c>: each resource once, the last declared first, also when a jump leaves the <c>using</c>; a failure of
/// <c>Dispose</c> replaces the failure in flight; nothing after the failure runs. Runs
/// <see cref="UsingFailureKernels"/> on the managed and native paths (Tests.Cpu); CUDA does not dispose the resource of
/// a failing thread yet (#1770).
/// </summary>
/// <remarks>
/// The order lists the <c>Dispose</c> calls: <c>id</c> when one starts, <c>-id</c> when it ends. A failure of the
/// body is <see cref="InvalidOperationException"/>, of an initializer <see cref="ArgumentOutOfRangeException"/>, of a
/// <c>Dispose</c> with fail code 1 <see cref="ArgumentException"/> and with fail code 2
/// <see cref="ArgumentOutOfRangeException"/>.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
[Collection(TestCollections.DeviceExclusive)]
public class UsingFailureTests
{
    private const DiffPath CpuPaths = DiffPath.Managed | DiffPath.Native;

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 0, null, new[] { 1, -1 }, 1, 7)]
    [InlineData(1, 0, typeof(InvalidOperationException), new[] { 1, -1 }, 0, 0)]
    [InlineData(0, 1, typeof(ArgumentException), new[] { 1 }, 1, 0)]
    [InlineData(1, 1, typeof(ArgumentException), new[] { 1 }, 0, 0)]
    public void Single_BodyOrDisposeFails_DisposesOnceAndThrowsTheLastFailure(int bodyFail, int disposeFail,
        Type? exception, int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1],
            (log, source, outs) => UsingFailureKernels.LaunchSingleKernel(log, source, outs, bodyFail, disposeFail));

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 0, 0, null, new[] { 2, -2, 1, -1 }, 1, 7)]
    [InlineData(1, 0, 0, typeof(InvalidOperationException), new[] { 2, -2, 1, -1 }, 0, 0)]
    [InlineData(1, 0, 2, typeof(ArgumentOutOfRangeException), new[] { 2, 1, -1 }, 0, 0)]
    [InlineData(1, 1, 2, typeof(ArgumentException), new[] { 2, 1 }, 0, 0)]
    [InlineData(0, 0, 2, typeof(ArgumentOutOfRangeException), new[] { 2, 1, -1 }, 1, 0)]
    [InlineData(0, 1, 2, typeof(ArgumentException), new[] { 2, 1 }, 1, 0)]
    public void Nested_BodyOrDisposeFails_DisposesInnerFirstAndThrowsTheLastFailure(int bodyFail, int failA, int failB,
        Type? exception, int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1],
            (log, source, outs) => UsingFailureKernels.LaunchNestedKernel(log, source, outs, bodyFail, failA, failB));

    // outs[0] is the value the device function returned
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, 0, 0, 0, null, new[] { 2, -2, 1, -1 }, 25)]
    [InlineData(true, 0, 0, 0, null, new[] { 1, -1 }, 15)]
    [InlineData(false, 1, 0, 0, typeof(InvalidOperationException), new[] { 2, -2, 1, -1 }, 0)]
    [InlineData(false, 0, 0, 2, typeof(ArgumentOutOfRangeException), new[] { 2, 1, -1 }, 0)]
    [InlineData(true, 0, 1, 0, typeof(ArgumentException), new[] { 1 }, 0)]
    [InlineData(false, 0, 1, 2, typeof(ArgumentException), new[] { 2, 1 }, 0)]
    public void Return_LeavesUsingDeclarations_DisposesEachOnceThenReturnsTheValue(bool early, int bodyFail,
        int failA, int failB, Type? exception, int[] order, int returned) =>
        AssertUsing(exception, order, [returned, 0],
            (log, source, outs) =>
                UsingFailureKernels.LaunchReturnKernel(log, source, outs, 5, early, bodyFail, failA, failB));

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(1, 3, -1, -1, null, new[] { 1, -1, 2, -2, 3, -3, 4, -4 }, 2, 7)]
    [InlineData(-1, -1, 2, -1, typeof(InvalidOperationException), new[] { 1, -1, 2, -2, 3, -3 }, 1, 0)]
    [InlineData(1, -1, -1, 1, typeof(ArgumentException), new[] { 1, -1, 2 }, 0, 0)]
    [InlineData(-1, 2, -1, 2, typeof(ArgumentException), new[] { 1, -1, 2, -2, 3 }, 1, 0)]
    [InlineData(-1, -1, -1, 0, typeof(ArgumentException), new[] { 1 }, 0, 0)]
    public void Loop_ContinueBreakOrFailure_DisposesEachPassOnce(int skip, int stop, int failAt, int disposeFailAt,
        Type? exception, int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1],
            (log, source, outs) =>
                UsingFailureKernels.LaunchLoopKernel(log, source, outs, 5, skip, stop, failAt, disposeFailAt));

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 0, 0, 0, null, new[] { 2, -2, 1, -1 }, 1, 7)]
    [InlineData(4, 0, 0, 0, typeof(ArgumentOutOfRangeException), new[] { 1, -1 }, 0, 0)]
    [InlineData(0, 1, 0, 0, typeof(InvalidOperationException), new[] { 2, -2, 1, -1 }, 0, 0)]
    [InlineData(0, 1, 0, 2, typeof(ArgumentOutOfRangeException), new[] { 2, 1, -1 }, 0, 0)]
    public void Declarators_LaterInitializerOrBodyFails_DisposesTheDeclaredResources(int initFailB, int bodyFail,
        int failA, int failB, Type? exception, int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1],
            (log, source, outs) =>
                UsingFailureKernels.LaunchDeclaratorsKernel(log, source, outs, initFailB, bodyFail, failA, failB));

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, null, new[] { 4, -4 }, 1, 7)]
    [InlineData(1, typeof(InvalidOperationException), new[] { 4, -4 }, 0, 0)]
    public void Expression_BodyFails_DisposesTheCopy(int bodyFail, Type? exception, int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1],
            (log, source, outs) => UsingFailureKernels.LaunchExpressionKernel(log, source, outs, bodyFail));

    // Runs the launch on each CPU path of this build and checks the exception, the Dispose order and outs[0..1]
    private static void AssertUsing(Type? exception, int[] order, int[] outs, Action<int[], int[], int[]> launch)
    {
        var selected = DiffPaths.Available.Where(p => (p.Path & CpuPaths) != 0).ToArray();
        Skip.If(selected.Length == 0, "CUDA does not dispose the resource of a failing thread yet (#1770)");
        foreach (var path in selected)
        {
            path.Preflight?.Invoke();
            var log = new int[32];
            int[] source = [3, 1, 2];
            var actualOuts = new int[3];
            var thrown = Record.Exception(() => path.Run(() => launch(log, source, actualOuts)));
            Assert.True(thrown?.GetType() == exception,
                $"{path.Name}: expected {exception?.Name ?? "no exception"}, got {thrown?.GetType().Name ?? "none"}: " +
                thrown?.Message);
            var actualOrder = log.Skip(1).Take(log[0]).ToArray();
            Assert.True(order.SequenceEqual(actualOrder),
                $"{path.Name}: expected the order [{string.Join(", ", order)}], " +
                $"got [{string.Join(", ", actualOrder)}]");
            Assert.True(outs.SequenceEqual(actualOuts.Take(2)),
                $"{path.Name}: expected outs [{string.Join(", ", outs)}], " +
                $"got [{string.Join(", ", actualOuts.Take(2))}]");
        }
    }
}
