using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// What decides whether an operand evaluated before a split is spilled, and how the spill is recorded.
/// </summary>
/// <param name="Others">What the operands that could change it read and write.</param>
/// <param name="Later">
/// What the operands evaluated after it read and write (a ref struct stays unless these write it).
/// </param>
/// <param name="Role">Why it is evaluated before the split.</param>
/// <param name="ForcedByConditional">
/// Whether only a split inside a conditionally evaluated operand comes after it.
/// </param>
internal readonly record struct IrSpillContext(ExpressionEffects Others, ExpressionEffects Later, IrSpillRole Role,
    bool ForcedByConditional)
{
    /// <summary>
    /// The context of the parts of a location: an argument of a call that splits keeps its role, any other location is
    /// a target.
    /// </summary>
    public IrSpillContext ForParts() =>
        Role is IrSpillRole.SplitArgument or IrSpillRole.SplitReceiver
            ? this
            : this with { Role = IrSpillRole.TargetPart };
}
