namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.ResumeLocals;

/// <summary>A hoisted local that a state machine declares again on every resume, and how.</summary>
/// <param name="Declarator">The declarator of the local in the lowered body (with its original-span annotation).</param>
/// <param name="Type">Its type.</param>
/// <param name="Kind">How it gets its storage and value.</param>
/// <param name="ElementType">The element type of a <see cref="ResumeLocalKind.StackAlloc"/> local.</param>
/// <param name="Length">The element count of a <see cref="ResumeLocalKind.StackAlloc"/> local.</param>
/// <param name="Unsupported">The node to report for a <see cref="ResumeLocalKind.Unsupported"/> local.</param>
internal readonly record struct ResumeLocal(
    VariableDeclaratorSyntax Declarator,
    ITypeSymbol Type,
    ResumeLocalKind Kind,
    ITypeSymbol? ElementType,
    int Length,
    SyntaxNode? Unsupported = null);
