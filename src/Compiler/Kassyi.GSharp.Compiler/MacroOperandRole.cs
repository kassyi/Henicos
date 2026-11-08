namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The place of an argument in the call of an operator macro's target (<see cref="MacroCall"/>). The operands are
/// numbered in the C# order of evaluation, so an array of their effects or temporaries is indexed by the role.
/// </summary>
internal enum MacroOperandRole
{
    /// <summary>The context variable, passed before the result.</summary>
    Context = -1,

    /// <summary>The assignment target, or the temporary of an inner operator macro.</summary>
    Result = 0,

    /// <summary>The left (or only) operand of the operator.</summary>
    Left = 1,

    /// <summary>The right operand of a binary operator other than a shift.</summary>
    Right = 2,

    /// <summary>The count of a shift.</summary>
    Extra = 3
}
