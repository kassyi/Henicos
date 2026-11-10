#if GSHARP_CPU
using Kassyi.GSharp.Runtime.Aot;
#endif

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A call that C# removes for <c>[Conditional]</c> does not run on any path, its arguments included, and a call that C#
/// keeps runs: decided by the symbols of the file the call was written in.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class ConditionalCallTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ConditionalCall_InKernel_IsRemoved() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallKernels.LaunchRemovedInKernel(data, blockDimX: 2);

            // No Overwrite (99) or HostTrace (77)
            Assert.Equal([11, 21], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ConditionalCall_InDeviceFunction_IsRemoved() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallKernels.LaunchRemovedInDeviceFunction(data, blockDimX: 2);

            Assert.Equal([11, 21], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ConditionalCall_ArgumentsWithSideEffects_AreNotEvaluated() =>
        OnEveryPath(static () =>
        {
            int[] data = [1, 2];

            ConditionalCallKernels.LaunchRemovedArguments(data, blockDimX: 2);

            // x++ would add 10, Bump 100
            Assert.Equal([11, 22], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalCall_InExpressionBodiesAndIncrementor_IsRemoved() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallKernels.LaunchRemovedInExpressionBodies(data, blockDimX: 2);

            Assert.Equal([22, 32], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalCall_SuspendingOrShuffleArgument_DoesNotRun() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallKernels.LaunchRemovedSplitArguments(data, blockDimX: 2);

            Assert.Equal([11, 21], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalCall_AsEmbeddedStatement_IsRemoved() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallKernels.LaunchRemovedAsEmbeddedStatements(data, blockDimX: 2);

            Assert.Equal([11, 21], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalCall_OneSymbolDefined_IsKept() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallKernels.LaunchKeptWhenDefined(data, blockDimX: 2);

            Assert.Equal([3011, 3021], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConditionalCall_FileDefineAndUndef_DecideTheCall() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallDirectiveKernels.LaunchFileDirectivesKernel(data, blockDimX: 2);

            // +1, +10 of the symbol the file defines, no +1000 of the symbol it undefines
            Assert.Equal([21, 31], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DirectiveBlock_InFileThatUndefinesTheSymbol_StaysInactive() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallDirectiveKernels.LaunchUndefinedBlockKernel(data, blockDimX: 2);

            Assert.Equal([11, 21], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DirectiveBlock_EndingAtTheClosingBrace_RunsItsActiveBranch() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallDirectiveKernels.LaunchBlockAtEndKernel(data, blockDimX: 2);

            Assert.Equal([16, 26], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DirectiveBlock_InPartWithoutTheDefineOfAnotherPart_StaysInactive() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            ConditionalCallDirectiveKernels.LaunchOtherPartBlockKernel(data, blockDimX: 2);

            Assert.Equal([11, 21], data);
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
