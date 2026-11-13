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
        AssertUsing(exception, order, [out0, out1, 0],
            (log, source, outs) => UsingFailureKernels.LaunchSingleKernel(log, source, outs, bodyFail, disposeFail));

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 0, 0, null, new[] { 2, -2, 1, -1 }, 1, 7, 5)]
    [InlineData(1, 0, 0, typeof(InvalidOperationException), new[] { 2, -2, 1, -1 }, 0, 0, 0)]
    [InlineData(1, 0, 2, typeof(ArgumentOutOfRangeException), new[] { 2, 1, -1 }, 0, 0, 0)]
    [InlineData(1, 1, 2, typeof(ArgumentException), new[] { 2, 1 }, 0, 0, 0)]
    [InlineData(0, 0, 2, typeof(ArgumentOutOfRangeException), new[] { 2, 1, -1 }, 1, 0, 0)]
    [InlineData(0, 1, 2, typeof(ArgumentException), new[] { 2, 1 }, 1, 0, 0)]
    public void Nested_BodyOrDisposeFails_DisposesInnerFirstAndThrowsTheLastFailure(int bodyFail, int failA, int failB,
        Type? exception, int[] order, int out0, int out1, int out2) =>
        AssertUsing(exception, order, [out0, out1, out2],
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
        AssertUsing(exception, order, [returned, 0, 0],
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
        AssertUsing(exception, order, [out0, out1, 0],
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
        AssertUsing(exception, order, [out0, out1, 0],
            (log, source, outs) =>
                UsingFailureKernels.LaunchDeclaratorsKernel(log, source, outs, initFailB, bodyFail, failA, failB));

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, null, new[] { 4, -4 }, 1, 7)]
    [InlineData(1, typeof(InvalidOperationException), new[] { 4, -4 }, 0, 0)]
    public void Expression_BodyFails_DisposesTheCopy(int bodyFail, Type? exception, int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1, 0],
            (log, source, outs) => UsingFailureKernels.LaunchExpressionKernel(log, source, outs, bodyFail));

    // A using declaration under a label, reached by a forward goto
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 0, null, new[] { 11, -11 }, 11, 7)]
    [InlineData(2, 0, null, new[] { 1, -1 }, 1, 7)]
    [InlineData(1, 0, typeof(InvalidOperationException), new[] { 11, -11 }, 0, 0)]
    [InlineData(0, 1, typeof(ArgumentException), new[] { 11 }, 11, 7)]
    [InlineData(1, 1, typeof(ArgumentException), new[] { 11 }, 0, 0)]
    public void LabeledDeclaration_BodyOrDisposeFails_DisposesOnceWithoutEndingTheProcess(int bodyFail,
        int disposeFail, Type? exception, int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1, 0],
            (log, source, outs) =>
                UsingFailureKernels.LaunchLabeledDeclarationKernel(log, source, outs, bodyFail, disposeFail));

    // goto case, goto default and break out of a using in a switch section
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 0, 0, null, new[] { 1, -1 }, 20, 7)]
    [InlineData(1, 0, 0, null, new[] { 2, -2 }, 52, 7)]
    [InlineData(3, 0, 0, null, new[] { 3, -3 }, 3, 7)]
    [InlineData(0, 0, 1, typeof(ArgumentException), new[] { 1 }, 0, 0)]
    [InlineData(0, 1, 1, typeof(ArgumentException), new[] { 1 }, 0, 0)]
    [InlineData(1, 0, 1, typeof(ArgumentException), new[] { 2 }, 2, 0)]
    public void Switch_GotoCaseOrBreakLeavesUsing_DisposesBeforeTheJump(int mode, int bodyFail, int disposeFail,
        Type? exception, int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1, 0],
            (log, source, outs) =>
                UsingFailureKernels.LaunchSwitchKernel(log, source, outs, mode, bodyFail, disposeFail));

    // continue (from a switch inside the body) and break leave two nested usings in a loop
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(2, -1, null, new[] { 20, -20, 10, -10, 21, -21, 11, -11, 22, -22, 12, -12 }, 1, 7, 1)]
    [InlineData(-1, -1, null,
        new[] { 20, -20, 10, -10, 21, -21, 11, -11, 22, -22, 12, -12, 23, -23, 13, -13 }, 8, 7, 3)]
    [InlineData(2, 1, typeof(ArgumentException), new[] { 20, -20, 10, -10, 21, 11, -11 }, 1, 0, 1)]
    [InlineData(2, 2, typeof(ArgumentException),
        new[] { 20, -20, 10, -10, 21, -21, 11, -11, 22, 12, -12 }, 1, 0, 1)]
    public void NestedLoop_BreakOrContinueLeavesTwoUsings_DisposesBoth(int stop, int disposeFailAt, Type? exception,
        int[] order, int out0, int out1, int out2) =>
        AssertUsing(exception, order, [out0, out1, out2],
            (log, source, outs) =>
                UsingFailureKernels.LaunchNestedLoopKernel(log, source, outs, 4, stop, disposeFailAt));

    // gotos out of two usings, two of them to the same label, one backwards
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 0, null, new[] { 11, -11, 1, -1, 12, -12, 2, -2 }, 101, 7)]
    [InlineData(1, 0, null, new[] { 11, -11, 1, -1, 12, -12, 2, -2 }, 0, 7)]
    [InlineData(3, 0, null, new[] { 11, -11, 1, -1, 12, -12, 2, -2 }, 1, 7)]
    [InlineData(0, 1, typeof(ArgumentException), new[] { 11, 1, -1 }, 0, 0)]
    public void Goto_LeavesTwoUsings_DisposesBothBeforeTheLabel(int mode, int disposeFail, Type? exception,
        int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1, 0],
            (log, source, outs) => UsingFailureKernels.LaunchGotoKernel(log, source, outs, mode, disposeFail));

    // A by-ref return out of a using declaration: the reference is kept while the resource is disposed
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 0, null, new[] { 1, -1 }, 42, 0)]
    [InlineData(1, 0, null, new[] { 1, -1 }, 0, 42)]
    [InlineData(1, 1, typeof(ArgumentException), new[] { 1 }, 0, 0)]
    public void RefReturn_LeavesUsing_WritesThroughTheKeptReference(int index, int disposeFail, Type? exception,
        int[] order, int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1, 0],
            (log, source, outs) => UsingFailureKernels.LaunchRefReturnKernel(log, source, outs, index, disposeFail));

    // A block lambda that returns a value out of its own using, called in the body of another using
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, null, new[] { 5, -5, 1, -1 }, 10, 7)]
    [InlineData(1, null, new[] { 5, -5, 1, -1 }, 1, 0)]
    [InlineData(2, null, new[] { 5, -5, 1, -1 }, 3, 7)]
    public void Lambda_ReturnsValueOutOfItsUsing_DisposesBothResources(int mode, Type? exception, int[] order,
        int out0, int out1) =>
        AssertUsing(exception, order, [out0, out1, 0],
            (log, source, outs) => UsingFailureKernels.LaunchLambdaKernel(log, source, outs, mode, 0));

    // Runs the launch on each CPU path of this build and checks the exception, the Dispose order and outs
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
            Assert.True(outs.SequenceEqual(actualOuts),
                $"{path.Name}: expected outs [{string.Join(", ", outs)}], " +
                $"got [{string.Join(", ", actualOuts)}]");
        }
    }
}
