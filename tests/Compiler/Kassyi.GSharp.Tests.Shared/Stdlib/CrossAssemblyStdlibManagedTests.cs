#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// The kernels of <see cref="CrossAssemblyStdlibKernels"/> on the managed fallback, which calls the standard library's
/// C# methods and the managed state machine of its suspending function (#538). The values are those of
/// <see cref="CrossAssemblyStdlibTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
[Collection(TestCollections.GlobalStateSequential)]
public class CrossAssemblyStdlibManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_MontgomeryMu_OfTheStandardLibrary()
    {
        var moduli = CrossAssemblyStdlibCases.Moduli();
        var result = new uint[moduli.Length];

        ManagedKernelRun.Run(() =>
            CrossAssemblyStdlibKernels.LaunchMontgomeryMuKernel(moduli, result, blockDimX: (uint)moduli.Length));

        CrossAssemblyStdlibCases.AssertMontgomeryMu(moduli, result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_WarpScanGp_SuspendingFunctionOfTheStandardLibrary()
    {
        var (generate, propagate) = CrossAssemblyStdlibCases.CarryBits();
        var result = new uint[32];

        ManagedKernelRun.Run(() =>
            CrossAssemblyStdlibKernels.LaunchWarpScanKernel(generate, propagate, result, blockDimX: 32));

        Assert.Equal(CrossAssemblyStdlibCases.ScanCarries(generate, propagate), result);
    }
}
#endif
