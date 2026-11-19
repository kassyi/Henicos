#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Corpus-wide agreement checks between IR null/call decisions and syntax-based helpers, and between pre-split and
/// post-split IR decisions (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrDecisionNullAgreementTests(ITestOutputHelper output)
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_NullAndCallDecisionsAgreeWithSyntax()
    {
        var counts = new IrNullAgreementCounts();

        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                IrNullAgreementRunner.ComparePreSplit(compilation, symbol, built, counts);
            }
        });

        WritePreSplitSummary(counts);
        Assert.True(counts.ElementCount > 0 && counts.AssignmentCount > 0 && counts.NullTestCount > 0 &&
                    counts.CoalesceCount > 0 && counts.MemberCount > 0 && counts.CallCount > 0,
            "Expected non-zero comparison counts for every null and call decision kind.");
        Assert.True(counts.Mismatches.IsEmpty,
            string.Join("\n", counts.Mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EverySplitMethod_PostSplitDecisionsAgreeWithPreSplit()
    {
        var counts = new IrNullAgreementCounts();
        var comparedMethods = 0;
        var comparedNodes = 0;

        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                var nodes = IrNullAgreementRunner.ComparePostSplit(compilation, symbol, built, counts);
                if (nodes >= 0)
                {
                    Interlocked.Increment(ref comparedMethods);
                    Interlocked.Add(ref comparedNodes, nodes);
                }
            }
        });

        output.WriteLine(
            $"Post-split null and call decisions compared: {comparedMethods} methods, {comparedNodes} nodes, " +
            $"SpillShifts={counts.SpillShifts.Count}");
        foreach (var shift in counts.SpillShifts.OrderBy(static s => s, StringComparer.Ordinal).Take(30))
        {
            output.WriteLine($"  SpillShift: {shift}");
        }

        Assert.True(comparedMethods > 0 && comparedNodes > 0, "Expected non-zero post-split comparisons.");
        Assert.True(counts.Mismatches.IsEmpty,
            string.Join("\n", counts.Mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    private void WritePreSplitSummary(IrNullAgreementCounts counts)
    {
        output.WriteLine(
            $"Compared null and call decisions: element {counts.ElementCount}, assignment {counts.AssignmentCount}, " +
            $"nullTest {counts.NullTestCount}, coalesce {counts.CoalesceCount}, member {counts.MemberCount}, " +
            $"call {counts.CallCount}, argCheck {counts.ArgumentCheckCount}, " +
            $"argConv {counts.ArgumentConversionCount}, managed {counts.ManagedCount}");
        output.WriteLine($"FrameVariableOnLegacyPath ({counts.FrameVariables.Count}):");
        foreach (var entry in counts.FrameVariables.OrderBy(static e => e, StringComparer.Ordinal))
        {
            output.WriteLine($"  {entry}");
        }

        output.WriteLine(
            "IsEmittedDeviceFunction differs from Target == DeviceFunction " +
            $"({counts.EmittedTargetDifferences.Count}):");
        foreach (var entry in counts.EmittedTargetDifferences.OrderBy(static e => e, StringComparer.Ordinal).Take(30))
        {
            output.WriteLine($"  {entry}");
        }

        output.WriteLine("Excluded shapes:");
        foreach (var pair in counts.ExcludedReasons.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  {pair.Key}: {pair.Value}");
        }
    }
}
