namespace Kassyi.GSharp.Compiler;

/// <summary>What an argument of the call of an operator macro's target is (<see cref="MacroArgument"/>).</summary>
internal enum MacroArgumentKind
{
    /// <summary>The context variable (<see cref="MacroCall.ContextVariable"/>).</summary>
    Context,

    /// <summary>An operand as written, or the result when it is the assignment target.</summary>
    Operand,

    /// <summary>An operand that is an operator macro itself, computed into a temporary of its own first.</summary>
    NestedOperand,

    /// <summary>The result of an inner operator macro: the temporary its outer call computes it into.</summary>
    InnerResult
}
