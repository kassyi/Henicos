namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Why a planned statement is not rewritten on the IR and stays on the syntax-based lowering: a form the plan decides
/// but whose rewrite the syntax-based stages after it cannot take yet. Unlike <see cref="IrSpillFallback"/>, a skip is
/// not a limit of the plan.
/// </summary>
internal enum IrSpillApplySkip
{
    /// <summary>The statement is rewritten.</summary>
    None,

    /// <summary>
    /// The right operand of a <c>??</c> holds a split: the syntax-based lowering does not write <c>??</c> as an
    /// <see langword="if"/>, and the stages after it have no syntax for one.
    /// </summary>
    CoalesceHoldsSplit,

    /// <summary>
    /// The receiver of an extension method that splits, called on it, is evaluated before the split: the syntax-based
    /// lowering keeps it in place.
    /// </summary>
    SplitExtensionReceiver,

    /// <summary>
    /// An operand evaluated before a split is an implicit user-defined conversion, which a copy of the operand's syntax
    /// would run after the split.
    /// </summary>
    UserDefinedImplicitConversionSpill
}
