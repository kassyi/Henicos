#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>Differential tests of <c>nint</c> and <c>nuint</c> in device code.</summary>
public class NativeIntegerDifferentialTests
{
    private const int Rows = 64;

    internal static readonly DifferentialCase s_constants = new(
        $"{nameof(NativeIntegerDifferentialKernels)}.{nameof(NativeIntegerDifferentialKernels.NativeIntegerConstantKernel)}")
    {
        Shape = new LaunchShape(1, 1, 1, 1, 1, 1, 1),
        Inputs = static (rng, rows) => new DiffBuffers()
            .Out<long>("dst", 6, ["shifted_constant", "cast_of_int", "unsigned_shift", "unsigned_wrap", "unsigned_product", "max_shifted"], OutputPolicy.Exact),
        Launch = static (b, s) => NativeIntegerDifferentialKernels.LaunchNativeIntegerConstantKernel(
            b.Get<long>("dst"), 123456, gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_arithmetic = new(
        $"{nameof(NativeIntegerDifferentialKernels)}.{nameof(NativeIntegerDifferentialKernels.NativeIntegerArithmeticKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", Array.ConvertAll(DiffValues.Int32(rng, rows), static v => (long)v << 20 ^ v))
            .Out<long>("dst", rows, ["add", "divide", "remainder", "unsigned_product", "unsigned_quotient", "shifted"], OutputPolicy.Exact),
        Launch = static (b, s) => NativeIntegerDifferentialKernels.LaunchNativeIntegerArithmeticKernel(
            b.Get<long>("a"), b.Get<long>("dst"), s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_ConstantsAndCasts_MatchTheOracleOnEveryPath() => DifferentialRunner.Run(s_constants);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_Arithmetic_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_arithmetic);
}
