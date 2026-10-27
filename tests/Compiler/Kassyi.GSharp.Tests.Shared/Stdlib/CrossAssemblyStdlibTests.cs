using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// The kernels of <see cref="CrossAssemblyStdlibKernels"/> on the native target (CPU or CUDA): they call the device
/// functions of Kassyi.GSharp.Stdlib.GpuMath, whose recorded text the native module imports (#433, #538). The managed
/// fallback runs the same kernels in <see cref="CrossAssemblyStdlibManagedTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class CrossAssemblyStdlibTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_MontgomeryMu_OfTheStandardLibrary()
    {
        var moduli = CrossAssemblyStdlibCases.Moduli();
        var result = new uint[moduli.Length];

        CrossAssemblyStdlibKernels.LaunchMontgomeryMuKernel(moduli, result, blockDimX: (uint)moduli.Length);

        CrossAssemblyStdlibCases.AssertMontgomeryMu(moduli, result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_WarpScanGp_SuspendingFunctionOfTheStandardLibrary()
    {
        var (generate, propagate) = CrossAssemblyStdlibCases.CarryBits();
        var result = new uint[32];

        CrossAssemblyStdlibKernels.LaunchWarpScanKernel(generate, propagate, result, blockDimX: 32);

        Assert.Equal(CrossAssemblyStdlibCases.ScanCarries(generate, propagate), result);
    }
}

/// <summary>The inputs and the properties of <see cref="CrossAssemblyStdlibTests"/>.</summary>
internal static class CrossAssemblyStdlibCases
{
    // Odd moduli, the Montgomery condition; 1 and the largest are the edges
    public static uint[] Moduli() => [1u, 3u, 5u, 7u, 0x8000_0001u, 0xDEAD_BEEFu, 0x1234_5679u, 0xFFFF_FFFFu];

    // n0 * mu == -1 (mod 2^32) (the property of CooperativeMathTests)
    public static void AssertMontgomeryMu(uint[] moduli, uint[] result)
    {
        Assert.Equal(1431655765u, result[1]);
        for (var i = 0; i < moduli.Length; i++)
        {
            Assert.Equal(uint.MaxValue, unchecked(moduli[i] * result[i]));
        }
    }

    // A warp of generate and propagate bits in which carries start, run and stop
    public static (uint[] Generate, uint[] Propagate) CarryBits()
    {
        var generate = new uint[32];
        var propagate = new uint[32];
        for (var lane = 0; lane < 32; lane++)
        {
            generate[lane] = lane % 7 == 0 ? 1u : 0u;
            propagate[lane] = lane % 5 == 4 ? 0u : 1u;
        }

        return (generate, propagate);
    }

    // The inclusive prefix of the carry operator, lane by lane: G = g | (p & G'), P = p & P', packed as (P << 1) | G
    public static uint[] ScanCarries(uint[] generate, uint[] propagate)
    {
        var result = new uint[generate.Length];
        uint g = 0;
        uint p = 1;
        for (var lane = 0; lane < generate.Length; lane++)
        {
            g = generate[lane] | (propagate[lane] & g);
            p = propagate[lane] & p;
            result[lane] = (p << 1) | g;
        }

        return result;
    }
}
