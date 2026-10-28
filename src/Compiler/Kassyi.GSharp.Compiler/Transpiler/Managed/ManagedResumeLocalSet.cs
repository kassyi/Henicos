namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The resume locals of a managed state machine (<see cref="ManagedContextFields"/>), as
/// <see cref="ContextLocalRewriter"/> looks them up. The initialization that stays where one is declared is an
/// assignment, or the binding to its stackalloc storage, zeroed again unless <c>[SkipLocalsInit]</c> applies
/// (<see cref="ResumeLocals.CreateInitialization"/>).
/// </summary>
/// <param name="locals">The resume locals by declaration.</param>
/// <param name="zeroFill">Whether a stackalloc gives zeroed memory (<see cref="SymbolExtensions.AreLocalsZeroed"/> of the method).</param>
internal sealed class ManagedResumeLocalSet(Dictionary<DeclarationKey, ManagedContextField> locals, bool zeroFill)
    : IResumeLocalSet
{
    public bool Contains(DeclarationKey key) => locals.ContainsKey(key);

    public StatementSyntax? CreateInitialization(DeclarationKey key, SimpleNameSyntax name, ExpressionSyntax value)
    {
        var local = locals[key];
        if (local.Resume is not { } resume)
        {
            return null;
        }

        var storage = resume.Kind == ResumeLocalKind.StackAlloc ? SyntaxFactory.IdentifierName(local.StorageName) : null;
        return ResumeLocals.CreateInitialization(resume, name, value, storage, zeroFill);
    }
}
