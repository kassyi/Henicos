namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// A hoisted context field with the type that <see cref="HoistedFieldTypeResolver"/> resolved for it and its name in
/// the managed state machine; for a local declared again on every resume, how it is declared (<see cref="ResumeLocals"/>).
/// </summary>
internal readonly record struct ManagedContextField(
    VariableDeclaratorSyntax Declarator,
    TypeSyntax DeclaredType,
    ITypeSymbol ResolvedType,
    string Name,
    ResumeLocal? Resume = null)
{
    /// <summary>The managed stackalloc storage of the resume local named <paramref name="name"/>.</summary>
    public static string GetStorageName(string name) =>
        GSharpInternalNames.Lowering.ResumeStoragePrefix + name.TrimStart('@');

    /// <summary>The managed stackalloc storage that a resume local is bound to where it is declared.</summary>
    public string StorageName => GetStorageName(Name);
}
