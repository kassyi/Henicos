#nullable enable
using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.DeviceExclusive)]
public class SharedMemberErrorSiteRuntimeTests
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedDisposeSwitch_MatchedMode_DisposesAndCompletesOnBothKernels()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();

            var log1 = new int[16];
            var outs1 = new int[1];
            path.Run(() => SharedMemberErrorSiteRuntimeKernels.LaunchFirstKernel(
                log1, outs1, mode: 0, blockDimX: 1));
            Assert.Equal(1, log1[1]);
            Assert.Equal(1, log1[9]);
            Assert.Equal(1, outs1[0]);

            var log2 = new int[16];
            var outs2 = new int[1];
            path.Run(() => SharedMemberErrorSiteRuntimeKernels.LaunchSecondKernel(
                log2, outs2, mode: 0, blockDimX: 1));
            Assert.Equal(1, log2[2]);
            Assert.Equal(1, log2[10]);
            Assert.Equal(2, outs2[0]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SharedDisposeSwitch_UnmatchedMode_ThrowsSwitchExpressionAtDisposeOnBothKernels()
    {
        var paths = DiffPaths.Available;
        Skip.If(paths.Count == 0, "No execution path available.");

        foreach (var path in paths)
        {
            path.Preflight?.Invoke();

            AssertKernelThrows(
                path,
                (log, outs) => SharedMemberErrorSiteRuntimeKernels.LaunchFirstKernel(
                    log, outs, mode: 1, blockDimX: 1));
            AssertKernelThrows(
                path,
                (log, outs) => SharedMemberErrorSiteRuntimeKernels.LaunchSecondKernel(
                    log, outs, mode: 1, blockDimX: 1));
        }
    }

    private static void AssertKernelThrows(DiffPathRunner path, Action<int[], int[]> launch)
    {
        var log = new int[16];
        var outs = new int[1];

        var ex = Assert.Throws<SwitchExpressionException>(() => path.Run(() => launch(log, outs)));
        if (path.Path == DiffPath.Managed)
        {
            return;
        }

        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("Dispose", ex.Data["GSharp.MemberName"]);
        Assert.Equal("SharedMemberErrorSiteRuntimeKernels.cu.cs(16,25)", ex.Data["GSharp.SourceLocation"]);
        Assert.Contains("in Dispose at SharedMemberErrorSiteRuntimeKernels.cu.cs(16,25)", ex.Message);
    }
}
