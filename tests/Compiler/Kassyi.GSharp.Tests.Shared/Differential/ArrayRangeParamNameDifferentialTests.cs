#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using K = Kassyi.GSharp.Tests.Shared.Differential.ArrayRangeParamNameKernels;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A failed range check of an <c>Array</c> or span call throws the argument exception C# throws, with the parameter
/// name C# gives it (<see cref="ArgumentException.ParamName"/>, or none), on the managed, native and CUDA paths. The
/// oracle is the kernel's own C# body, and the harness compares the type and the parameter name of its exception.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Scenario(TestScenario.SadPath)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class ArrayRangeParamNameDifferentialTests
{
    private static readonly string[] s_callNames =
    [
        "FillStartPastEnd",
        "FillStartNegative",
        "FillCountPastEnd",
        "FillCountNegative",
        "CopySourceTooShort",
        "CopyDestinationTooShort",
        "CopyRangeSourceTooShort",
        "CopyRangeDestinationTooShort",
        "SpanCopyToShorter",
        "SpanSlicePastEnd",
        "CopyNegativeLength",
        "CopyNegativeSourceIndex",
        "CopyNegativeDestinationIndex",
        "CopySourceTooShortAndDestinationIndexNegative",
        "CopyBothIndexesNegative",
        "CopyNegativeLengthAndIndex",
        "FillStartAtEnd",
        "CopyEmptyAtEnd",
        "CopyEmptyDestinationAtEnd",
        "SliceStartAtEnd",
        "SliceCountToEnd",
    ];

    // Each call from the kernel (error sites of the kernel) and from a device function (messages)
    internal static readonly DifferentialCase[] s_cases =
    [
        .. Enumerable.Range(0, RangeCalls.Count).Select(static call => Create(call, inDeviceFunction: false)),
        .. Enumerable.Range(0, RangeCalls.Count).Select(static call => Create(call, inDeviceFunction: true)),
    ];

    /// <summary>The names of <see cref="s_cases"/>.</summary>
    public static TheoryData<string> CaseNames => [.. s_cases.Select(static c => c.Name)];

    [SkippableTheory]
    [MemberData(nameof(CaseNames))]
    public void ArrayRangeCall_FailedCheck_NamesTheParameterAsCSharpDoes(string caseName) =>
        DifferentialRunner.Run(s_cases.Single(c => c.Name == caseName));

    private static DifferentialCase Create(int call, bool inDeviceFunction) =>
        new(DiffKernelNames.Of(
            typeof(K),
            inDeviceFunction ? nameof(K.ArrayRangeDeviceFunctionKernel) : nameof(K.ArrayRangeCallKernel),
            s_callNames[call]))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            OracleExceptions = OracleExceptionMode.MatchOracle,
            // The range checks of an array or span call are made by a checked CUDA build only
            ExceptionPaths = DiffPaths.CheckedPaths(checkedBuildOnlyOnCuda: true),
            Inputs = static (_, _) => new DiffBuffers()
                .In("a", new[] { 1, 2, 3 }, readOnly: true)
                .In("s", new[] { 9 }, readOnly: true)
                .Out<int>("o", 2, OutputPolicy.Exact),
            Launch = (b, s) =>
            {
                if (inDeviceFunction)
                {
                    K.LaunchArrayRangeDeviceFunctionKernel(
                        b.Get<int>("a"), b.Get<int>("s"), call, b.Get<int>("o"), gridDimX: s.GridX, blockDimX: s.BlockX);
                }
                else
                {
                    K.LaunchArrayRangeCallKernel(
                        b.Get<int>("a"), b.Get<int>("s"), call, b.Get<int>("o"), gridDimX: s.GridX, blockDimX: s.BlockX);
                }
            },
            OracleIsLaunch = true,
        };
}
