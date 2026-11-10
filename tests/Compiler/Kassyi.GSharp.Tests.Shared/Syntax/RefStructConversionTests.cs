#if GSHARP_CPU
using Kassyi.GSharp.Runtime.Aot;
#endif

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class RefStructConversionTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ImplicitRefStructConversion_ValidInputValues_RunsBodyAndComputesExpectedValues() =>
        OnEveryPath(static () =>
        {
            int[] input = [3, -4];
            int[] fields = new int[input.Length];
            int[] converted = new int[input.Length];

            RefStructConversionKernels.LaunchImplicitRefStructConversionKernel(input, fields, converted, blockDimX: 2);

            // ScaledRef(int) stores x * 2; int(ScaledRef) returns V + 1
            Assert.Equal([6, -8], fields);
            Assert.Equal([7, -7], converted);
        });

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ExplicitRefStructConversion_ValidInputValues_RunsBodyAndComputesExpectedValues() =>
        OnEveryPath(static () =>
        {
            int[] input = [3, -4];
            int[] fields = new int[input.Length];
            int[] converted = new int[input.Length];

            RefStructConversionKernels.LaunchExplicitRefStructConversionKernel(input, fields, converted, blockDimX: 2);

            // ExplicitScaledRef(int) stores x * 3; int(ExplicitScaledRef) returns V - 1
            Assert.Equal([9, -12], fields);
            Assert.Equal([8, -13], converted);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RefStructToRefStructConversion_ValidInputValues_RunsBodyAndComputesPadsAndConversions() =>
        OnEveryPath(static () =>
        {
            int[] input = [3, -4];
            int[] pads = new int[input.Length];
            int[] converted = new int[input.Length];
            int[] back = new int[input.Length];

            RefStructConversionKernels.LaunchRefStructToRefStructConversionKernel(
                input,
                pads,
                converted,
                back,
                blockDimX: 2
            );

            // ZetaRef(AlphaRef) stores Pad = 9 and W = V * 5; AlphaRef(ZetaRef) returns W - Pad
            Assert.Equal([9, 9], pads);
            Assert.Equal([15, -20], converted);
            Assert.Equal([6, -29], back);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void MixedRefStructConversion_BodyAndMacroConversions_RunsBodyAndInitializesFields() =>
        OnEveryPath(static () =>
        {
            int[] input = [3, -4];
            int[] fields = new int[input.Length];

            RefStructConversionKernels.LaunchMixedRefStructConversionKernel(input, fields, blockDimX: 2);

            // MixedRef(int) stores x * 2
            Assert.Equal([6, -8], fields);
        });

    // The native build of the dialect, and on the CPU the managed path as well
    private static void OnEveryPath(Action check)
    {
        check();
#if GSHARP_CPU
        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            check();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
#endif
    }
}
