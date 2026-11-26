#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of <c>nint</c> and <c>nuint</c> as a constant, a struct field, an element of a span and an index
/// in device code, on the managed path, the native CPU path and CUDA.
/// </summary>
public class NativeIntegerStorageDifferentialTests
{
    private const int Rows = 64;

    private static readonly long[] s_values =
    [
        0, 1, -1, 2, -2, 7, long.MinValue, long.MaxValue, 1L << 32, -(1L << 32), 1L << 62, 0x0123_4567_89AB_CDEF
    ];

    private static readonly IReadOnlyList<LaunchShape> s_shapes =
        [LaunchShapes.Linear(Rows, 7), LaunchShapes.Linear(Rows, 64)];

    private static long[] Values(int rows, int shift)
    {
        var result = new long[rows];
        for (int i = 0; i < rows; i++)
        {
            result[i] = s_values[(i + shift) % s_values.Length];
        }

        return result;
    }

    private static DifferentialCase Unary(
        Int64RowsLaunch launch,
        int outputPerRow,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (_, shape) => new DiffBuffers()
                .In("a", Values(shape.Rows, 0), readOnly: true)
                .Out<long>("dst", shape.Rows * outputPerRow, OutputPolicy.Exact),
            shapes: s_shapes,
            launchExpression: launchExpression);

    private static DifferentialCase Binary(
        Int64PairRowsLaunch launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (_, shape) => new DiffBuffers()
                .In("a", Values(shape.Rows, 0), readOnly: true)
                .In("b", Values(shape.Rows, 5), readOnly: true)
                .Out<long>("dst", shape.Rows * 2, OutputPolicy.Exact),
            shapes: s_shapes,
            launchExpression: launchExpression);

    internal static readonly DifferentialCase s_negativeConstant = Unary(
        NativeIntegerStorageKernels.LaunchNativeIntegerNegativeConstantKernel, 1);

    internal static readonly DifferentialCase s_wideConstant = Unary(
        NativeIntegerStorageKernels.LaunchNativeIntegerWideConstantKernel, 1);

    internal static readonly DifferentialCase s_index = Unary(
        NativeIntegerStorageKernels.LaunchNativeIntegerIndexKernel, 1);

    internal static readonly DifferentialCase s_structField = Binary(
        NativeIntegerStorageKernels.LaunchNativeIntegerStructFieldKernel);

    internal static readonly DifferentialCase s_spanElement = Binary(
        NativeIntegerStorageKernels.LaunchNativeIntegerSpanElementKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NegativeConstant_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_negativeConstant);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_ConstantAboveInt32_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_wideConstant);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_AsArrayIndex_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_index);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_StructField_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_structField);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_SpanElement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanElement);
}
