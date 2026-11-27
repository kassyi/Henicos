#nullable enable
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Cpu.Syntax;

/// <summary>
/// Verifies that a conditional expression (<c>?:</c>) with an operator macro on one branch and a
/// <c>Warp.ActiveMask()</c> split point on the other compiles and produces exact C# values across all CPU paths
/// (managed, native, managed stack, native stack).
/// </summary>
[Category(TestCategory.Differential)]
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class OperatorMacroTernarySplitTests
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TernaryOperator_MacroBranchAndActiveMaskBranch_MatchesCSharpValuesOnAllCpuPaths()
    {
        int[] input = [1, -1, 0, 2, -3, 0, 5, -2];
        // Lanes 1, 2, 4, 5, 7 enter the false branch -> mask 0b10110110 = 182
        int[] expectedOutput = [12, 182, 182, 22, 182, 182, 52, 182];
        int[] expectedCount = [0, 1, 1, 0, 1, 1, 0, 1];

        foreach (DiffPathRunner path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            int[] output = new int[expectedOutput.Length];
            int[] count = new int[expectedCount.Length];
            path.Run(() => OperatorMacroTernarySplitKernels.LaunchTernaryMacroWithActiveMaskKernel(
                input, output, count, blockDimX: 8));
            Assert.True(
                expectedOutput.SequenceEqual(output),
                $"{path.Name} output: expected [{string.Join(", ", expectedOutput)}], got [{string.Join(", ", output)}]");
            Assert.True(
                expectedCount.SequenceEqual(count),
                $"{path.Name} count: expected [{string.Join(", ", expectedCount)}], got [{string.Join(", ", count)}]");
        }
    }
}
