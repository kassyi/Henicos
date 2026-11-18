namespace Kassyi.GSharp.Compiler.Ir.Frames;

/// <summary>One tracked variable: its index, where and how it is declared, and which states name it.</summary>
/// <param name="variable">
/// The tracked variable (<see cref="IrVariableKind.Local"/> or <see cref="IrVariableKind.Temporary"/>).
/// </param>
/// <param name="index">The zero-based variable index in declaration order.</param>
/// <param name="declaration">The top-level statement position of its declaration.</param>
/// <param name="form">How the variable is declared.</param>
/// <param name="referencingStates">
/// The sorted, deduplicated state numbers that reference or declare the variable.
/// </param>
internal sealed class IrLivenessVariable(IrVariable variable, int index, IrStatePosition declaration,
    IrDeclarationForm form, ImmutableArray<int> referencingStates)
{
    /// <summary>The tracked variable.</summary>
    public IrVariable Variable { get; } = variable;

    /// <summary>The zero-based variable index in declaration order.</summary>
    public int Index { get; } = index;

    /// <summary>The top-level statement position of its declaration.</summary>
    public IrStatePosition Declaration { get; } = declaration;

    /// <summary>How the variable is declared.</summary>
    public IrDeclarationForm Form { get; } = form;

    /// <summary>The sorted, deduplicated state numbers that reference or declare the variable.</summary>
    public ImmutableArray<int> ReferencingStates { get; } = referencingStates;
}
