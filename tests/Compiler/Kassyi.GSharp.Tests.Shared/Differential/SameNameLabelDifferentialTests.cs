#nullable enable
using System;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Labels of one name in sibling blocks run as C# binds them: each goto loops in its own block, on every path.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class SameNameLabelDifferentialTests
{
    private const int Lanes = 32;

    // Even lanes loop twice in the first block, odd lanes three times; every lane three times in the second
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SiblingLabels_SameNameAcrossABarrier_EachGotoLoopsInItsOwnBlock()
    {
        var expected = Enumerable.Range(0, Lanes).Select(static t => (2 + (t & 1)) * 10 + 3).ToArray();
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var output = new int[Lanes];
            path.Run(() => SameNameLabelDifferentialKernels.LaunchSiblingLabelsKernel(output, blockDimX: Lanes));
            Assert.True(expected.SequenceEqual(output),
                $"{path.Name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", output)}]");
        }
    }
}
