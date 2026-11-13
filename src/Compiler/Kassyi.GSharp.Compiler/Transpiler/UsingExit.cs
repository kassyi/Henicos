namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// One way out of the body of a <c>using</c>: the jumps of one kind to one target share it, and its exit label disposes
/// the resource and then jumps on as <see cref="Jump"/> does.
/// </summary>
/// <param name="Kind">The kind of the jump.</param>
/// <param name="Key">
/// What the jumps that share the exit have in common: the function for a <c>return</c>, the loop or switch for a
/// <c>break</c> or <c>continue</c>, the label for a <c>goto</c>, the jump itself for a <c>goto case</c>.
/// </param>
/// <param name="StateMachineReturn">
/// Whether a <c>return</c> is one that lowering wrote for a state machine (it writes no termination).
/// </param>
/// <param name="Jump">The first jump of the exit, which the exit label writes again after the <c>Dispose</c>.</param>
internal readonly record struct UsingExit(
    UsingExitKind Kind,
    SyntaxNode Key,
    bool StateMachineReturn,
    StatementSyntax Jump
);
