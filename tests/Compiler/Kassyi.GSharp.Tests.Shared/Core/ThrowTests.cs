#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// <c>throw</c> in device code ends the launch with an exception of the same type, with the place of the throw, on the
/// managed and native paths and on CUDA: nothing after the throwing statement runs, the arguments of the exception are
/// evaluated first.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
[Collection(TestCollections.DeviceExclusive)]
public class ThrowTests
{
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, null, null, 1, 2)]
    [InlineData(1, typeof(InvalidOperationException), "stop here", 1, 0)]
    [InlineData(2, typeof(FormatException), "bad format", 1, 0)]
    [InlineData(3, typeof(ArgumentException), null, 1, 0)]
    [InlineData(4, typeof(NotImplementedException), null, 1, 0)]
    public void Statement_ThrowsTheTypeAndStopsTheThread(int mode, Type? exception, string? text, int out0, int out1) =>
        AssertThrows(exception, text, [out0, out1, 0], (_, outs) => ThrowKernels.LaunchStatementKernel(outs, mode));

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(3, null, 3, 9)]
    [InlineData(-1, typeof(ArgumentOutOfRangeException), 0, 0)]
    public void Expression_InAConditionalOfADeviceFunction_ThrowsTheType(int v, Type? exception, int out0, int out1) =>
        AssertThrows(exception, null, [out0, out1, 0], (_, outs) => ThrowKernels.LaunchExpressionKernel(outs, v));

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(4, null, 4, 9)]
    [InlineData(0, typeof(InvalidOperationException), 0, 0)]
    public void Coalesce_NullOperand_ThrowsTheRightOperand(int v, Type? exception, int out0, int out1) =>
        AssertThrows(exception, exception is null ? null : "no value", [out0, out1, 0],
            (_, outs) => ThrowKernels.LaunchCoalesceKernel(outs, v));

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, null, 0, 9, 0)]
    [InlineData(1, typeof(ArgumentOutOfRangeException), 0, 0, 7)]
    public void Arguments_AreEvaluatedBeforeTheThrow(int mode, Type? exception, int out0, int out1, int out2) =>
        AssertThrows(exception, null, [out0, out1, out2], (_, outs) => ThrowKernels.LaunchArgumentsKernel(outs, mode));

    private static void AssertThrows(Type? exception, string? text, int[] expectedOuts, Action<int[], int[]> launch,
        int[]? order = null)
    {
        var selected = DiffPaths.Available.ToArray();
        Skip.If(selected.Length == 0, "No path of this build runs this launch");
        foreach (var path in selected)
        {
            path.Preflight?.Invoke();
            var outs = new int[3];
            var log = new int[32];
            var thrown = Record.Exception(() => path.Run(() => launch(log, outs)));
            Assert.True(thrown?.GetType() == exception,
                $"{path.Name}: expected {exception?.Name ?? "no exception"}, got {thrown?.GetType().Name ?? "none"}: " +
                thrown?.Message);
            if (text is not null)
            {
                Assert.Contains(text, thrown!.Message);
            }

            Assert.True(expectedOuts.SequenceEqual(outs),
                $"{path.Name}: expected outs [{string.Join(", ", expectedOuts)}], got [{string.Join(", ", outs)}]");
            if (order is not null)
            {
                var actual = log.Skip(1).Take(log[0]).ToArray();
                Assert.True(order.SequenceEqual(actual),
                    $"{path.Name}: expected the order [{string.Join(", ", order)}], got [{string.Join(", ", actual)}]");
            }
        }
    }
}
