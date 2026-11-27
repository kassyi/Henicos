namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Planned CUDA iteration keys for <c>Warp.ActiveMask()</c> calls inside loops of one method body.
/// </summary>
internal sealed class ActiveMaskIterationPlan
{
    public static readonly ActiveMaskIterationPlan Empty = new(
        ImmutableArray<KeyedLoop>.Empty,
        0,
        -1,
        new Dictionary<InvocationExpressionSyntax, ImmutableArray<IterationKey>>(),
        new Dictionary<TextSpan, ImmutableArray<IterationKey>>(),
        new Dictionary<StatementSyntax, ImmutableArray<int>>(),
        new Dictionary<LabeledStatementSyntax, ImmutableArray<int>>(),
        new Dictionary<SwitchStatementSyntax, ImmutableArray<int>>(),
        new Dictionary<StatementSyntax, int>(),
        new Dictionary<LabeledStatementSyntax, ImmutableArray<int>>(),
        new Dictionary<SwitchSectionSyntax, int>());

    private readonly int _treeOrdinal;
    private readonly Dictionary<InvocationExpressionSyntax, ImmutableArray<IterationKey>> _callKeys;
    private readonly Dictionary<TextSpan, ImmutableArray<IterationKey>> _callKeysBySpan;
    private readonly Dictionary<StatementSyntax, ImmutableArray<int>> _resetCounters;
    private readonly Dictionary<LabeledStatementSyntax, ImmutableArray<int>> _postLabelResetCounters;
    private readonly Dictionary<SwitchStatementSyntax, ImmutableArray<int>> _switchResets;
    private readonly Dictionary<StatementSyntax, int> _loopCounters;
    private readonly Dictionary<LabeledStatementSyntax, ImmutableArray<int>> _labelCounters;
    private readonly Dictionary<SwitchSectionSyntax, int> _switchSectionCounters;

    internal ActiveMaskIterationPlan(
        ImmutableArray<KeyedLoop> keyedLoops,
        int counterCount,
        int treeOrdinal,
        Dictionary<InvocationExpressionSyntax, ImmutableArray<IterationKey>> callKeys,
        Dictionary<TextSpan, ImmutableArray<IterationKey>> callKeysBySpan,
        Dictionary<StatementSyntax, ImmutableArray<int>> resetCounters,
        Dictionary<LabeledStatementSyntax, ImmutableArray<int>> postLabelResetCounters,
        Dictionary<SwitchStatementSyntax, ImmutableArray<int>> switchResets,
        Dictionary<StatementSyntax, int> loopCounters,
        Dictionary<LabeledStatementSyntax, ImmutableArray<int>> labelCounters,
        Dictionary<SwitchSectionSyntax, int> switchSectionCounters)
    {
        KeyedLoops = keyedLoops;
        CounterCount = counterCount;
        _treeOrdinal = treeOrdinal;
        _callKeys = callKeys;
        _callKeysBySpan = callKeysBySpan;
        _resetCounters = resetCounters;
        _postLabelResetCounters = postLabelResetCounters;
        _switchResets = switchResets;
        _loopCounters = loopCounters;
        _labelCounters = labelCounters;
        _switchSectionCounters = switchSectionCounters;
    }

    /// <summary>All keyed loops of the method, ordered from outermost to innermost.</summary>
    public ImmutableArray<KeyedLoop> KeyedLoops { get; }

    /// <summary>Number of 64-bit <c>Counter</c> locals to declare at the start of the method body.</summary>
    public int CounterCount { get; }

    /// <summary>
    /// Gets the iteration keys that apply to <paramref name="call"/>, ordered outermost loop first.
    /// </summary>
    public bool TryGetKeys(InvocationExpressionSyntax call, out ImmutableArray<IterationKey> keys)
    {
        if (_callKeys.TryGetValue(call, out keys))
        {
            return true;
        }

        if (_treeOrdinal >= 0 && _callKeysBySpan.Count > 0)
        {
            foreach (var annotation in call.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
            {
                if (LoweringAnnotations.TryParseOriginalSpanData(
                        annotation.Data,
                        out var start,
                        out var length,
                        out var treeOrdinal,
                        out var region) &&
                    region == 0 &&
                    treeOrdinal == _treeOrdinal &&
                    _callKeysBySpan.TryGetValue(new TextSpan(start, length), out keys))
                {
                    return true;
                }
            }
        }

        keys = ImmutableArray<IterationKey>.Empty;
        return false;
    }

    /// <summary>Gets the counter indices that must be reset immediately before <paramref name="statement"/>.</summary>
    public bool TryGetResetCounters(StatementSyntax statement, out ImmutableArray<int> counters)
    {
        if (_resetCounters.TryGetValue(statement, out counters))
        {
            return true;
        }

        counters = ImmutableArray<int>.Empty;
        return false;
    }

    /// <summary>Gets the counter indices that must be reset immediately after <paramref name="label"/>.</summary>
    public bool TryGetPostLabelResetCounters(LabeledStatementSyntax label, out ImmutableArray<int> counters)
    {
        if (_postLabelResetCounters.TryGetValue(label, out counters))
        {
            return true;
        }

        counters = ImmutableArray<int>.Empty;
        return false;
    }

    /// <summary>Gets the counter indices that must be reset before <paramref name="switchStatement"/>.</summary>
    public bool TryGetSwitchResets(SwitchStatementSyntax switchStatement, out ImmutableArray<int> counters)
    {
        if (_switchResets.TryGetValue(switchStatement, out counters))
        {
            return true;
        }

        counters = ImmutableArray<int>.Empty;
        return false;
    }

    /// <summary>Gets the counter index incremented at the top of structured <paramref name="loop"/>'s body.</summary>
    public bool TryGetLoopCounter(StatementSyntax loop, out int counterIndex) =>
        _loopCounters.TryGetValue(loop, out counterIndex);

    /// <summary>Gets the counter indices incremented immediately after <paramref name="label"/>.</summary>
    public bool TryGetLabelCounters(LabeledStatementSyntax label, out ImmutableArray<int> counters)
    {
        if (_labelCounters.TryGetValue(label, out counters))
        {
            return true;
        }

        counters = ImmutableArray<int>.Empty;
        return false;
    }

    /// <summary>Gets the counter index incremented at the entry of cyclic <paramref name="section"/>.</summary>
    public bool TryGetSwitchSectionCounter(SwitchSectionSyntax section, out int counterIndex) =>
        _switchSectionCounters.TryGetValue(section, out counterIndex);
}
