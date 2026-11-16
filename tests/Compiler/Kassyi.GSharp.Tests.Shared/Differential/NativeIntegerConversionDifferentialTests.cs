#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of the conversions between <c>nint</c> or <c>nuint</c> and <c>int</c>, <c>uint</c>, <c>long</c>,
/// <c>ulong</c>, <c>float</c> and <c>double</c> in device code, on the managed path, the native CPU path and CUDA.
/// </summary>
public class NativeIntegerConversionDifferentialTests
{
    private const int Rows = 64;

    private static readonly long[] s_longs =
    [
        0, 1, -1, 2, -2, 255, 65535, int.MinValue, int.MaxValue, uint.MaxValue, 1L << 32, -(1L << 32),
        long.MinValue, long.MaxValue, 1L << 62, 1L << 53, (1L << 53) + 1, 0x0123_4567_89AB_CDEF
    ];

    // In range of nint; the values of s_doublesUnsigned also fit nuint
    private static readonly double[] s_doubles =
    [
        0, 0.5, -0.5, 1.5, -1.5, 2.9, -2.9, 123456789.75, -123456789.75, 1e10, -1e10, 4e18, -4e18, 9.2e18, -9.2e18
    ];

    private static readonly double[] s_doublesUnsigned =
        [0, 0.5, 1.5, 2.9, 123456789.75, 4294967295.5, 1e10, 4e18, 9.2e18, 9.3e18, 1.8e19];

    private static readonly IReadOnlyList<LaunchShape> s_shapes =
        [LaunchShapes.Linear(Rows, 7), LaunchShapes.Linear(Rows, 64)];

    private static T[] Cast<T>(long[] values, int rows, Func<long, T> convert)
    {
        var result = new T[rows];
        for (int i = 0; i < rows; i++)
        {
            result[i] = convert(values[i % values.Length]);
        }

        return result;
    }

    private static double[] Cast(double[] values, int rows) => Cast(values, rows, static v => v);

    private static T[] Cast<T>(double[] values, int rows, Func<double, T> convert)
    {
        var result = new T[rows];
        for (int i = 0; i < rows; i++)
        {
            result[i] = convert(values[i % values.Length]);
        }

        return result;
    }

    private static DifferentialCase Case<TSource, TResult>(string kernel, Func<int, TSource[]> source,
        Action<TSource[], TResult[], int, uint, uint> launch)
        where TSource : unmanaged
        where TResult : unmanaged =>
        new($"{nameof(NativeIntegerConversionKernels)}.{kernel}Kernel")
        {
            Shapes = s_shapes,
            Inputs = (rng, shape) => new DiffBuffers()
                .In("a", source(shape.Rows), readOnly: true)
                .Out<TResult>("dst", shape.Rows, OutputPolicy.Exact),
            Launch = (b, s) => launch(b.Get<TSource>("a"), b.Get<TResult>("dst"), s.Rows, s.GridX, s.BlockX),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_nintToFloat = Case<long, float>(
        "NintToFloat",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintToFloatKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintToFloat_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintToFloat);

    internal static readonly DifferentialCase s_nintToDouble = Case<long, double>(
        "NintToDouble",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintToDoubleKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintToDouble_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintToDouble);

    internal static readonly DifferentialCase s_nintToInt = Case<long, int>(
        "NintToInt",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintToIntKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintToInt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintToInt);

    internal static readonly DifferentialCase s_nintToUInt = Case<long, uint>(
        "NintToUInt",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintToUIntKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintToUInt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintToUInt);

    internal static readonly DifferentialCase s_nintToULong = Case<long, ulong>(
        "NintToULong",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintToULongKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintToULong_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintToULong);

    internal static readonly DifferentialCase s_nuintToFloat = Case<long, float>(
        "NuintToFloat",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintToFloatKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintToFloat_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintToFloat);

    internal static readonly DifferentialCase s_nuintToDouble = Case<long, double>(
        "NuintToDouble",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintToDoubleKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintToDouble_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintToDouble);

    internal static readonly DifferentialCase s_nuintToInt = Case<long, int>(
        "NuintToInt",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintToIntKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintToInt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintToInt);

    internal static readonly DifferentialCase s_nuintToUInt = Case<long, uint>(
        "NuintToUInt",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintToUIntKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintToUInt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintToUInt);

    internal static readonly DifferentialCase s_nuintToULong = Case<long, ulong>(
        "NuintToULong",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintToULongKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintToULong_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintToULong);

    internal static readonly DifferentialCase s_nintToLong = Case<long, long>(
        "NintToLong",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintToLongKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintToLong_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintToLong);

    internal static readonly DifferentialCase s_nintFromInt = Case<int, long>(
        "NintFromInt",
        static rows => Cast(s_longs, rows, static v => (int)v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintFromIntKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintFromInt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintFromInt);

    internal static readonly DifferentialCase s_nintFromUInt = Case<uint, long>(
        "NintFromUInt",
        static rows => Cast(s_longs, rows, static v => (uint)v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintFromUIntKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintFromUInt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintFromUInt);

    internal static readonly DifferentialCase s_nintFromULong = Case<ulong, long>(
        "NintFromULong",
        static rows => Cast(s_longs, rows, static v => (ulong)v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintFromULongKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintFromULong_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintFromULong);

    internal static readonly DifferentialCase s_nintFromFloat = Case<float, long>(
        "NintFromFloat",
        static rows => Cast(s_doubles, rows, static v => (float)v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintFromFloatKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintFromFloat_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintFromFloat);

    internal static readonly DifferentialCase s_nintFromDouble = Case<double, long>(
        "NintFromDouble",
        static rows => Cast(s_doubles, rows),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNintFromDoubleKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NintFromDouble_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nintFromDouble);

    internal static readonly DifferentialCase s_nuintFromInt = Case<int, ulong>(
        "NuintFromInt",
        static rows => Cast(s_longs, rows, static v => (int)v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintFromIntKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintFromInt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintFromInt);

    internal static readonly DifferentialCase s_nuintFromUInt = Case<uint, ulong>(
        "NuintFromUInt",
        static rows => Cast(s_longs, rows, static v => (uint)v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintFromUIntKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintFromUInt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintFromUInt);

    internal static readonly DifferentialCase s_nuintFromULong = Case<ulong, ulong>(
        "NuintFromULong",
        static rows => Cast(s_longs, rows, static v => (ulong)v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintFromULongKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintFromULong_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintFromULong);

    internal static readonly DifferentialCase s_nuintFromFloat = Case<float, ulong>(
        "NuintFromFloat",
        static rows => Cast(s_doublesUnsigned, rows, static v => (float)v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintFromFloatKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintFromFloat_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintFromFloat);

    internal static readonly DifferentialCase s_nuintFromDouble = Case<double, ulong>(
        "NuintFromDouble",
        static rows => Cast(s_doublesUnsigned, rows),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintFromDoubleKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintFromDouble_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintFromDouble);

    internal static readonly DifferentialCase s_nuintFromLong = Case<long, ulong>(
        "NuintFromLong",
        static rows => Cast(s_longs, rows, static v => v),
        static (a, d, n, g, x) =>
            NativeIntegerConversionKernels.LaunchNuintFromLongKernel(a, d, n, gridDimX: g, blockDimX: x));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_NuintFromLong_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nuintFromLong);
}
