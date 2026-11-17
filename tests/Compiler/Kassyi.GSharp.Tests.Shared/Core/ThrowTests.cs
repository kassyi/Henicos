#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// <c>throw</c> in device code ends the launch with an exception of the same type, on the managed and native paths and
/// on CUDA: nothing after the throwing statement runs, the arguments of the exception are evaluated first, and the
/// thread that throws is the only one that stops. The plain C# method of each kernel is the oracle for the type, the
/// <c>ParamName</c>, the message (a prefix of it, since the CPU paths and CUDA append the place and the thread), the
/// outputs and, for a <c>using</c>, the <c>Dispose</c> log. A throw in a device function carries its place, message and
/// exception type as a throw in a kernel does (its error sites travel with the function to the kernels that call it).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
[Collection(TestCollections.DeviceExclusive)]
public class ThrowTests
{
    private const DiffPath CpuPaths = DiffPath.Managed | DiffPath.Native;
    private const string KernelFile = "ThrowKernels.cu.cs";

    /// <summary>A form of <c>throw</c>: the kernel as plain C# and as a launch, and what is compared.</summary>
    /// <param name="Kernel">The name of the kernel or device function that holds the throw, which the message of the native and CUDA paths names.</param>
    /// <param name="Oracle">Runs the kernel as C#.</param>
    /// <param name="Launch">Launches the kernel.</param>
    /// <param name="CompareMessage">Whether the message of the throw is a text that the paths carry.</param>
    private sealed record ThrowCase(
        string Kernel,
        Action<int[], int> Oracle,
        Action<int[], int> Launch,
        bool CompareMessage = true);

    private static readonly Dictionary<string, ThrowCase> s_forms = new()
    {
        ["Literal"] = new("LiteralKernel", ThrowKernels.LiteralKernel, (o, f) => ThrowKernels.LaunchLiteralKernel(o, f)),
        ["NoMessage"] = new("NoMessageKernel", ThrowKernels.NoMessageKernel,
            (o, f) => ThrowKernels.LaunchNoMessageKernel(o, f)),
        ["NotImplemented"] = new("NotImplementedKernel", ThrowKernels.NotImplementedKernel,
            (o, f) => ThrowKernels.LaunchNotImplementedKernel(o, f), CompareMessage: false),
        ["FrameworkType"] = new("FrameworkTypeKernel", ThrowKernels.FrameworkTypeKernel,
            (o, f) => ThrowKernels.LaunchFrameworkTypeKernel(o, f)),
        ["UserType"] = new("UserTypeKernel", ThrowKernels.UserTypeKernel,
            (o, f) => ThrowKernels.LaunchUserTypeKernel(o, f)),
        ["NestedType"] = new("NestedTypeKernel", ThrowKernels.NestedTypeKernel,
            (o, f) => ThrowKernels.LaunchNestedTypeKernel(o, f)),
        ["GenericType"] = new("GenericTypeKernel", ThrowKernels.GenericTypeKernel,
            (o, f) => ThrowKernels.LaunchGenericTypeKernel(o, f)),
        ["ConstMessage"] = new("ConstMessageKernel", ThrowKernels.ConstMessageKernel,
            (o, f) => ThrowKernels.LaunchConstMessageKernel(o, f)),
        ["ConstInterpolated"] = new("ConstInterpolatedKernel", ThrowKernels.ConstInterpolatedKernel,
            (o, f) => ThrowKernels.LaunchConstInterpolatedKernel(o, f)),
        ["ArgumentParamName"] = new("ArgumentParamNameKernel", ThrowKernels.ArgumentParamNameKernel,
            (o, f) => ThrowKernels.LaunchArgumentParamNameKernel(o, f)),
        ["ArgumentNullParamName"] = new("ArgumentNullParamNameKernel", ThrowKernels.ArgumentNullParamNameKernel,
            (o, f) => ThrowKernels.LaunchArgumentNullParamNameKernel(o, f)),
        ["ArgumentOutOfRangeParamName"] = new("ArgumentOutOfRangeParamNameKernel",
            ThrowKernels.ArgumentOutOfRangeParamNameKernel,
            (o, f) => ThrowKernels.LaunchArgumentOutOfRangeParamNameKernel(o, f)),
        ["NameofParamName"] = new("NameofParamNameKernel", ThrowKernels.NameofParamNameKernel,
            (o, f) => ThrowKernels.LaunchNameofParamNameKernel(o, f)),
        ["Arguments"] = new("ArgumentsKernel", ThrowKernels.ArgumentsKernel,
            (o, f) => ThrowKernels.LaunchArgumentsKernel(o, f)),
        ["ConditionalInDeviceFunction"] = new("Checked", ThrowKernels.ConditionalInDeviceFunctionKernel,
            (o, f) => ThrowKernels.LaunchConditionalInDeviceFunctionKernel(o, f)),
        ["ArrowBody"] = new("Always", ThrowKernels.ArrowBodyKernel,
            (o, f) => ThrowKernels.LaunchArrowBodyKernel(o, f)),
        ["SwitchArm"] = new("SwitchArmKernel", ThrowKernels.SwitchArmKernel,
            (o, f) => ThrowKernels.LaunchSwitchArmKernel(o, f)),
        ["CoalesceValue"] = new("CoalesceValueKernel", ThrowKernels.CoalesceValueKernel,
            (o, f) => ThrowKernels.LaunchCoalesceValueKernel(o, f)),
        ["CoalesceReference"] = new("CoalesceReferenceKernel", ThrowKernels.CoalesceReferenceKernel,
            (o, f) => ThrowKernels.LaunchCoalesceReferenceKernel(o, f)),
        ["Loop"] = new("LoopKernel", ThrowKernels.LoopKernel, (o, f) => ThrowKernels.LaunchLoopKernel(o, f)),
        // Gpu.SyncThreads needs a thread context, so the oracle is the same body without the barrier
        ["DeviceLiteral"] = new("ThrowsLiteral", ThrowKernels.DeviceLiteralKernel,
            (o, f) => ThrowKernels.LaunchDeviceLiteralKernel(o, f)),
        ["DeviceUserType"] = new("ThrowsUser", ThrowKernels.DeviceUserTypeKernel,
            (o, f) => ThrowKernels.LaunchDeviceUserTypeKernel(o, f)),
        ["Deep"] = new("Inner", ThrowKernels.DeepKernel, (o, f) => ThrowKernels.LaunchDeepKernel(o, f)),
        ["SharedFirst"] = new("Shared", ThrowKernels.SharedFirstKernel,
            (o, f) => ThrowKernels.LaunchSharedFirstKernel(o, f)),
        ["SharedSecond"] = new("Shared", ThrowKernels.SharedSecondKernel,
            (o, f) => ThrowKernels.LaunchSharedSecondKernel(o, f)),
        ["Suspending"] = new("Suspending", (o, f) =>
            {
                o[0] = 1;
                if (f != 0)
                {
                    throw new InvalidOperationException("after the barrier");
                }

                o[1] = 4;
                o[2] = 9;
            },
            (o, f) => ThrowKernels.LaunchSuspendingKernel(o, f))
    };

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Literal")]
    [InlineData("NoMessage")]
    [InlineData("NotImplemented")]
    [InlineData("FrameworkType")]
    [InlineData("UserType")]
    [InlineData("NestedType")]
    [InlineData("GenericType")]
    [InlineData("ConstMessage")]
    [InlineData("ConstInterpolated")]
    [InlineData("ArgumentParamName")]
    [InlineData("ArgumentNullParamName")]
    [InlineData("ArgumentOutOfRangeParamName")]
    [InlineData("NameofParamName")]
    [InlineData("Arguments")]
    [InlineData("ConditionalInDeviceFunction")]
    [InlineData("ArrowBody")]
    [InlineData("SwitchArm")]
    [InlineData("CoalesceValue")]
    [InlineData("CoalesceReference")]
    [InlineData("Loop")]
    [InlineData("DeviceLiteral")]
    [InlineData("DeviceUserType")]
    [InlineData("Deep")]
    [InlineData("SharedFirst")]
    [InlineData("SharedSecond")]
    [InlineData("Suspending")]
    public void Form_FailOrNot_MatchesCSharpOnEveryPath(string form)
    {
        var thrownCase = s_forms[form];
        var selected = DiffPaths.Available.ToArray();
        Skip.If(selected.Length == 0, "No path of this build runs this launch");
        foreach (var fail in new[] { 0, form == "Loop" ? 3 : 1 })
        {
            var expectedOuts = new int[4];
            var expected = Record.Exception(() => thrownCase.Oracle(expectedOuts, fail));
            Assert.True((fail != 0) == (expected is not null),
                $"{form}: the oracle {(expected is null ? "did not throw" : "threw")} for fail={fail}");
            foreach (var path in selected)
            {
                path.Preflight?.Invoke();
                var outs = new int[4];
                var thrown = Record.Exception(() => path.Run(() => thrownCase.Launch(outs, fail)));
                AssertSameFailure(path, thrownCase, form, fail, expected, thrown);
                Assert.True(expectedOuts.SequenceEqual(outs),
                    $"{path.Name} {form} fail={fail}: expected outs [{string.Join(", ", expectedOuts)}], got [{string.Join(", ", outs)}]");
            }
        }
    }

    // One device function with two throws, called from two kernels: each launch reports its own throw, at its own line
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SharedDeviceFunction_CalledFromTwoKernels_ReportsDistinctPlaces()
    {
        var selected = DiffPaths.Available.Where(p => p.Path != DiffPath.Managed).ToArray();
        Skip.If(selected.Length == 0, "No path of this build reports a place");
        foreach (var path in selected)
        {
            path.Preflight?.Invoke();
            var first = Record.Exception(() => path.Run(() => ThrowKernels.LaunchSharedFirstKernel(new int[4], 1)));
            var second = Record.Exception(() => path.Run(() => ThrowKernels.LaunchSharedSecondKernel(new int[4], 1)));
            Assert.NotNull(first);
            Assert.NotNull(second);
            var lines = new[] { PlaceLine(first.Message), PlaceLine(second.Message) };
            Assert.True(lines[0] != lines[1], $"{path.Name}: both launches name line {lines[0]}");
            Assert.Contains("shared one", first.Message, StringComparison.Ordinal);
            Assert.Contains("shared two", second.Message, StringComparison.Ordinal);
            Assert.Contains("\"shared one\"", ReadKernelLine(lines[0]), StringComparison.Ordinal);
            Assert.Contains("\"shared two\"", ReadKernelLine(lines[1]), StringComparison.Ordinal);
        }
    }

    // Every thread but thread 0 writes both of its entries; thread 0 stops between its two writes
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, new[] { 1, 10, 1, 11, 1, 12, 1, 13, 1, 14, 1, 15, 1, 16, 1, 17 })]
    [InlineData(1, new[] { 1, 0, 1, 11, 1, 12, 1, 13, 1, 14, 1, 15, 1, 16, 1, 17 })]
    public void OtherThreads_ThreadZeroThrows_RunToTheirEnd(int fail, int[] expected) =>
        AssertLaunch(fail, expected, outs => ThrowKernels.LaunchOtherThreadsKernel(outs, fail, gridDimX: 2, blockDimX: 4));

    // The block barrier does not wait for the thread that threw
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, new[] { 1, 10, 1, 11, 1, 12, 1, 13 })]
    [InlineData(1, new[] { 1, 0, 1, 11, 1, 12, 1, 13 })]
    public void Barrier_ThreadZeroThrowsBeforeIt_OthersPassIt(int fail, int[] expected) =>
        AssertLaunch(fail, expected, outs => ThrowKernels.LaunchBarrierKernel(outs, fail, blockDimX: 4));

    // The lane that threw has exited and takes no part in the ballot of the others
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void Ballot_LaneZeroThrows_CountsTheOtherLanes(int fail)
    {
        var expected = Enumerable.Repeat(fail == 0 ? -1 : unchecked((int)0xFFFFFFFEu), 32).ToArray();
        if (fail != 0)
        {
            expected[0] = 0;
        }

        AssertLaunch(fail, expected, outs => ThrowKernels.LaunchBallotKernel(outs, fail, blockDimX: 32));
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("UsingBody")]
    [InlineData("UsingNested")]
    [InlineData("UsingLoop")]
    public void Using_ThrowInTheBody_DisposesAsCSharpDoes(string form)
    {
        Action<int[], int[], int> oracle = form switch
        {
            "UsingBody" => ThrowKernels.UsingBodyKernel,
            "UsingNested" => ThrowKernels.UsingNestedKernel,
            _ => ThrowKernels.UsingLoopKernel
        };
        Action<int[], int[], int> launch = form switch
        {
            "UsingBody" => (l, o, f) => ThrowKernels.LaunchUsingBodyKernel(l, o, f),
            "UsingNested" => (l, o, f) => ThrowKernels.LaunchUsingNestedKernel(l, o, f),
            _ => (l, o, f) => ThrowKernels.LaunchUsingLoopKernel(l, o, f)
        };
        AssertUsing(form, oracle, launch, DiffPaths.Available.ToArray());
    }

    // A throw in a device function called inside a using: CUDA does not dispose the resource of a thread that fails in
    // a callee (the case of UsingFailureTests), so this runs on the CPU paths
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Using_ThrowInACallee_DisposesAsCSharpDoes() =>
        AssertUsing("UsingCallee", ThrowKernels.UsingCalleeKernel,
            (l, o, f) => ThrowKernels.LaunchUsingCalleeKernel(l, o, f),
            DiffPaths.Available.Where(p => (p.Path & CpuPaths) != 0).ToArray());

    private static void AssertUsing(string form, Action<int[], int[], int> oracle, Action<int[], int[], int> launch,
        DiffPathRunner[] selected)
    {
        Skip.If(selected.Length == 0, "No path of this build runs this launch");
        var thrownCase = new ThrowCase(form == "UsingCallee" ? "Throwing" : form + "Kernel", (_, _) => { }, (_, _) => { });
        foreach (var fail in new[] { 0, 1, 3 })
        {
            var expectedLog = new int[32];
            var expectedOuts = new int[4];
            var expected = Record.Exception(() => oracle(expectedLog, expectedOuts, fail));
            foreach (var path in selected)
            {
                path.Preflight?.Invoke();
                var log = new int[32];
                var outs = new int[4];
                var thrown = Record.Exception(() => path.Run(() => launch(log, outs, fail)));
                AssertSameFailure(path, thrownCase, form, fail, expected, thrown);
                Assert.True(expectedLog.SequenceEqual(log),
                    $"{path.Name} {form} fail={fail}: expected the Dispose log [{string.Join(", ", expectedLog.Take(expectedLog[0] + 1))}], got [{string.Join(", ", log.Take(log[0] + 1))}]");
                Assert.True(expectedOuts.SequenceEqual(outs),
                    $"{path.Name} {form} fail={fail}: expected outs [{string.Join(", ", expectedOuts)}], got [{string.Join(", ", outs)}]");
            }
        }
    }

    private static void AssertLaunch(int fail, int[] expected, Action<int[]> launch)
    {
        var selected = DiffPaths.Available.ToArray();
        Skip.If(selected.Length == 0, "No path of this build runs this launch");
        foreach (var path in selected)
        {
            path.Preflight?.Invoke();
            var outs = new int[expected.Length];
            var thrown = Record.Exception(() => path.Run(() => launch(outs)));
            Assert.True((fail != 0) == (thrown is InvalidOperationException),
                $"{path.Name}: fail={fail}, got {thrown?.GetType().Name ?? "no exception"}: {thrown?.Message}");
            Assert.True(expected.SequenceEqual(outs),
                $"{path.Name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", outs)}]");
        }
    }

    // The type, the parameter name, the message (a prefix of it) and the place of the failure that a path raised, against
    // the exception that C# threw
    private static void AssertSameFailure(DiffPathRunner path, ThrowCase thrownCase, string form, int fail,
        Exception? expected, Exception? thrown)
    {
        var label = $"{path.Name} {form} fail={fail}";
        Assert.True(thrown?.GetType() == expected?.GetType(),
            $"{label}: expected {expected?.GetType().Name ?? "no exception"}, got {thrown?.GetType().Name ?? "none"}: {thrown?.Message}");
        if (expected is null || thrown is null)
        {
            return;
        }

        if (expected is ArgumentException expectedArgument)
        {
            Assert.Equal(expectedArgument.ParamName, ((ArgumentException)thrown).ParamName);
        }

        if (thrownCase.CompareMessage)
        {
            if (path.Path == DiffPath.Managed)
            {
                Assert.Equal(expected.Message, thrown.Message);
            }
            else
            {
                Assert.StartsWith(WithoutParameterSuffix(expected), thrown.Message, StringComparison.Ordinal);
            }
        }

        if (path.Path != DiffPath.Managed)
        {
            AssertPlace(label, thrownCase, thrown.Message);
        }
    }

    // The message of the exception C# threw up to the " (Parameter 'x')" it appends for a parameter name (what follows it,
    // such as "Actual value was 7.", is not carried)
    private static string WithoutParameterSuffix(Exception exception)
    {
        var message = exception.Message;
        if (exception is not ArgumentException { ParamName: { Length: > 0 } name })
        {
            return message;
        }

        var at = message.IndexOf($" (Parameter '{name}')", StringComparison.Ordinal);
        return at < 0 ? message : message.AsSpan(0, at).ToString();
    }

    // The line of the place that a message names: ThrowKernels.cu.cs(<line>,<column>)
    private static int PlaceLine(string message)
    {
        var start = message.IndexOf(KernelFile + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the message names no place: {message}");
        var numbers = message.AsSpan(start + KernelFile.Length + 1);
        return int.Parse(numbers[..numbers.IndexOf(',')].ToString());
    }

    private static string ReadKernelLine(int line) =>
        File.ReadLines(RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Core", KernelFile))
            .ElementAt(line - 1);

    // The message names the file and the line of a throw: a kernel's statement, or the throw of a device function
    private static void AssertPlace(string label, ThrowCase thrownCase, string message)
    {
        Assert.Contains($"in {thrownCase.Kernel} at {KernelFile}(", message, StringComparison.Ordinal);

        var line = PlaceLine(message);
        var text = ReadKernelLine(line);
        Assert.True(text.Contains("throw", StringComparison.Ordinal) || text.Contains("=>", StringComparison.Ordinal),
            $"{label}: line {line} of {KernelFile} is not the throw: {text}");
    }
}
