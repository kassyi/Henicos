namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>One step that the split pass puts before a statement, in the C# order of evaluation.</summary>
/// <param name="kind">What the step does.</param>
/// <param name="node">The operand spilled, the call that splits, or the conditional expression.</param>
/// <param name="role">Why an operand is spilled; <see cref="IrSpillRole.None"/> for the other kinds.</param>
/// <param name="forcedByConditional">
/// Whether a spill exists only because a split inside a conditionally evaluated operand comes after it.
/// </param>
/// <param name="branches">
/// The steps of each branch of a conditional: one for <c>&amp;&amp;</c>, <c>||</c> and <c>??</c>, two for
/// <c>?:</c> (when true, when false); empty for the other kinds.
/// </param>
internal sealed class IrSpillStep(IrSpillStepKind kind, IrExpression node, IrSpillRole role, bool forcedByConditional,
    ImmutableArray<ImmutableArray<IrSpillStep>> branches)
{
    /// <summary>What the step does.</summary>
    public IrSpillStepKind Kind { get; } = kind;

    /// <summary>The operand spilled, the call that splits, or the conditional expression.</summary>
    public IrExpression Node { get; } = node;

    /// <summary>Why an operand is spilled; <see cref="IrSpillRole.None"/> for the other kinds.</summary>
    public IrSpillRole Role { get; } = role;

    /// <summary>
    /// Whether a spill exists only because a split inside a conditionally evaluated operand comes after it.
    /// </summary>
    public bool ForcedByConditional { get; } = forcedByConditional;

    /// <summary>The steps of each branch of a conditional; empty for the other kinds.</summary>
    public ImmutableArray<ImmutableArray<IrSpillStep>> Branches { get; } = branches;

    /// <summary>A spill of <paramref name="operand"/>.</summary>
    public static IrSpillStep Spill(IrExpression operand, IrSpillRole role, bool forcedByConditional) =>
        new(IrSpillStepKind.Spill, operand, role, forcedByConditional,
            ImmutableArray<ImmutableArray<IrSpillStep>>.Empty);

    /// <summary>The call <paramref name="call"/> splitting here.</summary>
    public static IrSpillStep Split(IrExpression call) => new(IrSpillStepKind.Split, call, IrSpillRole.None, false,
        ImmutableArray<ImmutableArray<IrSpillStep>>.Empty);

    /// <summary>The conditional expression <paramref name="conditional"/> written as an <see langword="if"/>.</summary>
    public static IrSpillStep Conditional(IrExpression conditional,
        ImmutableArray<ImmutableArray<IrSpillStep>> branches) =>
        new(IrSpillStepKind.Conditional, conditional, IrSpillRole.None, false, branches);
}
