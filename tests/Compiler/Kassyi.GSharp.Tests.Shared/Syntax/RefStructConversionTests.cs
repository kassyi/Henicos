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
    public void Test_ImplicitRefStructConversion_RunsItsBody() =>
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
    public void Test_ExplicitRefStructConversion_RunsItsBody() =>
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
