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
    UserDefinedImplicitConversionSpill,

    /// <summary>
    /// A node evaluated before a split, or replaced by a temporary, stands for no syntax of its own (a default
    /// argument, the array of a <see langword="params"/> argument, an implicit receiver), other than an implicit
    /// conversion without a user-defined operator: a copy of its syntax would be a different expression.
    /// </summary>
    SynthesizedOperand,

    /// <summary>
    /// An operand evaluated before a split has no type of its own (a target-typed <c>new()</c> or conditional, a
    /// collection expression, <c>default</c>): <c>var t = &lt;operand&gt;;</c> would not compile.
    /// </summary>
    NoNaturalType,

    /// <summary>
    /// An operand evaluated before a split lies inside a <c>checked(...)</c> or <c>unchecked(...)</c> expression, whose
    /// overflow context a copy of the operand out of its statement would not keep.
    /// </summary>
    OverflowContextOperand,

    /// <summary>
    /// The statement of the prepared body is not the statement of the source the plan was made for: an expansion, a
    /// removal or Stryker's switches changed it.
    /// </summary>
    PreparedSyntaxDiffers,

    /// <summary>
    /// The split annotations of the syntax do not agree with the splits of the IR: a call the plan splits has no
    /// matching annotation, or an annotated call is left inside an expression.
    /// </summary>
    SplitAnnotationMismatch,

    /// <summary>A node of the rewritten statement has no syntax in the prepared statement to copy.</summary>
    NotMapped
}
