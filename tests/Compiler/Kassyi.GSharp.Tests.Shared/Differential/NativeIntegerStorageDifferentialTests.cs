#nullable enable
using System;
using System.Collections.Generic;
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
        string kernel, int outputPerRow, Action<long[], long[], int, uint, uint> launch) =>
        new($"{nameof(NativeIntegerStorageKernels)}.{kernel}Kernel")
        {
            Shapes = s_shapes,
            Inputs = (rng, shape) => new DiffBuffers()
                .In("a", Values(shape.Rows, 0), readOnly: true)
                .Out<long>("dst", shape.Rows * outputPerRow, OutputPolicy.Exact),
            Launch = (b, s) => launch(b.Get<long>("a"), b.Get<long>("dst"), s.Rows, s.GridX, s.BlockX),
            OracleIsLaunch = true,
        };

    private static DifferentialCase Binary(string kernel, Action<long[], long[], long[], int, uint, uint> launch) =>
        new($"{nameof(NativeIntegerStorageKernels)}.{kernel}Kernel")
        {
            Shapes = s_shapes,
            Inputs = (rng, shape) => new DiffBuffers()
                .In("a", Values(shape.Rows, 0), readOnly: true)
                .In("b", Values(shape.Rows, 5), readOnly: true)
                .Out<long>("dst", shape.Rows * 2, OutputPolicy.Exact),
            Launch = (b, s) =>
                launch(b.Get<long>("a"), b.Get<long>("b"), b.Get<long>("dst"), s.Rows, s.GridX, s.BlockX),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_negativeConstant = Unary(
        "NativeIntegerNegativeConstant", 1,
        static (a, d, n, g, x) =>
            NativeIntegerStorageKernels.LaunchNativeIntegerNegativeConstantKernel(a, d, n, gridDimX: g, blockDimX: x));

    internal static readonly DifferentialCase s_wideConstant = Unary(
        "NativeIntegerWideConstant", 1,
        static (a, d, n, g, x) =>
            NativeIntegerStorageKernels.LaunchNativeIntegerWideConstantKernel(a, d, n, gridDimX: g, blockDimX: x));

    internal static readonly DifferentialCase s_index = Unary(
        "NativeIntegerIndex", 1,
        static (a, d, n, g, x) =>
            NativeIntegerStorageKernels.LaunchNativeIntegerIndexKernel(a, d, n, gridDimX: g, blockDimX: x));

    internal static readonly DifferentialCase s_structField = Binary(
        "NativeIntegerStructField",
        static (a, b, d, n, g, x) =>
            NativeIntegerStorageKernels.LaunchNativeIntegerStructFieldKernel(a, b, d, n, gridDimX: g, blockDimX: x));

    internal static readonly DifferentialCase s_spanElement = Binary(
        "NativeIntegerSpanElement",
        static (a, b, d, n, g, x) =>
            NativeIntegerStorageKernels.LaunchNativeIntegerSpanElementKernel(a, b, d, n, gridDimX: g, blockDimX: x));

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
