#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Thread-safe counters and mismatch bags for the corpus agreement tests of the null and call decisions (#1561).
/// </summary>
internal sealed class IrNullAgreementCounts
{
    public ConcurrentBag<string> Mismatches { get; } = [];
    public ConcurrentBag<string> FrameVariables { get; } = [];
    public ConcurrentBag<string> SpillShifts { get; } = [];
    public ConcurrentBag<string> EmittedTargetDifferences { get; } = [];
    public ConcurrentDictionary<string, int> ExcludedReasons { get; } = new(StringComparer.Ordinal);

    public int ElementCount;
    public int AssignmentCount;
    public int NullTestCount;
    public int CoalesceCount;
    public int MemberCount;
    public int CallCount;
    public int ArgumentCheckCount;
    public int ArgumentConversionCount;
    public int ManagedCount;

    public void AddExcluded(string reason) =>
        ExcludedReasons.AddOrUpdate(reason, 1, static (_, count) => count + 1);
}
