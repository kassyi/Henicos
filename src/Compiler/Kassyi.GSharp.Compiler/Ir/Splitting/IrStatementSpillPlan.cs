namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>What the split pass does before one statement that holds a split.</summary>
/// <param name="statement">The statement.</param>
/// <param name="fallback">
/// Why the statement stays on the syntax-based lowering; <see cref="IrSpillFallback.None"/> when it is planned.
/// </param>
/// <param name="steps">The steps, in the C# order of evaluation; empty for a fallback.</param>
internal sealed class IrStatementSpillPlan(IrStatement statement, IrSpillFallback fallback,
    ImmutableArray<IrSpillStep> steps)
{
    /// <summary>The statement.</summary>
    public IrStatement Statement { get; } = statement;

    /// <summary>
    /// Why the statement stays on the syntax-based lowering; <see cref="IrSpillFallback.None"/> when it is planned.
    /// </summary>
    public IrSpillFallback Fallback { get; } = fallback;

    /// <summary>The steps, in the C# order of evaluation; empty for a fallback.</summary>
    public ImmutableArray<IrSpillStep> Steps { get; } = steps;
}
