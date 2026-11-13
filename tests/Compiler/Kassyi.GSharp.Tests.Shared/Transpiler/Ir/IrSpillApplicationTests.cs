#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Runs the split pass of the IR in front of the syntax passes on every GPU method of the generator snapshot inputs
/// and of every kernel file of the shared tests (#1561) and checks that it writes what the plans say, leaves every
/// other statement as the syntax passes make it, and is left as it is by them (<see cref="IrSpillApplication"/>).
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrSpillApplicationTests(ITestOutputHelper output)
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_WithTheIrInFrontOfTheSyntaxPasses_WritesThePlansAndLeavesTheRest()
    {
        var stopwatch = Stopwatch.StartNew();
        var application = new IrSpillApplication();

        IrCoverageInputs.ForEach(application.CheckAll, everyKernelFile: true);

        output.WriteLine($"{application.Lowered} statements written by the IR, {application.ComparedUnchanged} " +
                         $"left to the syntax passes compared, {stopwatch.ElapsedMilliseconds} ms");
        foreach (var (reason, count) in application.Reasons.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"left to the syntax passes, {reason}: {count}");
        }

        output.WriteLine($"{application.MixedMethods.Count} methods mix statements written by the IR and statements " +
                         "with splits left to the syntax passes");
        Assert.True(application.Lowered > 100, $"Only {application.Lowered} statements written by the IR.");
        Assert.True(application.ComparedUnchanged > 100,
            $"Only {application.ComparedUnchanged} statements compared with the syntax passes.");
        Assert.True(application.Failures.Count == 0,
            string.Join("\n", application.Failures.OrderBy(static m => m, StringComparer.Ordinal).Take(30)));
    }
}
