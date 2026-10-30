namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The locals that a state machine declares again on every resume (<see cref="ResumeLocals"/>), as
/// <see cref="ContextLocalRewriter"/> looks them up: the native and the managed state machines keep different details of
/// them.
/// </summary>
internal interface IResumeLocalSet
{
    /// <summary>Whether the declaration <paramref name="key"/> is a resume local.</summary>
    bool Contains(DeclarationKey key);

    /// <summary>
    /// The statement that replaces the initialization of the resume local <paramref name="key"/> where it is declared,
    /// given the reference to the local and the rewritten initializer (<see cref="ResumeLocals.CreateInitialization"/>);
    /// <see langword="null"/> removes the initialization.
    /// </summary>
    StatementSyntax? CreateInitialization(DeclarationKey key, SimpleNameSyntax name, ExpressionSyntax value);
}
