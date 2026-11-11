namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Why an operand is evaluated into a temporary before a split (<see cref="IrSpillStepKind.Spill"/>).
/// </summary>
internal enum IrSpillRole
{
    /// <summary>Not a spill.</summary>
    None,

    /// <summary>
    /// An argument of a call that splits, or a part of the location of one: the call runs again on every resume, so
    /// each argument is evaluated once, before it.
    /// </summary>
    SplitArgument,

    /// <summary>
    /// The receiver of a call that splits, also the first argument of an extension method called as one
    /// (<c>x.M()</c>): the call runs again on every resume, so the receiver is evaluated once, before it.
    /// </summary>
    SplitReceiver,

    /// <summary>The left operand of a binary operator whose right operand holds a split.</summary>
    LeftOperand,

    /// <summary>
    /// An operand evaluated before the operand that holds a split, other than a left operand: an earlier argument of a
    /// call that does not split, a receiver, an index, an earlier argument or initializer of a creation.
    /// </summary>
    EarlierOperand,

    /// <summary>
    /// A part of a location evaluated before a split that is not an argument of a call that splits: the array or the
    /// index of an assignment target, of the target of a compound assignment, or of a by-reference argument.
    /// </summary>
    TargetPart,

    /// <summary>
    /// The value of the target of a compound assignment, which C# reads before it evaluates the right operand. The
    /// assignment is then written as <c>target = temporary op value</c>, with the conversions of the compound form.
    /// </summary>
    CompoundRead
}
