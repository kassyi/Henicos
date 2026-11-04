namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Declares at the start of their statement list the locals that a <c>goto</c> jumps past (<see cref="GotoJumpAnalysis"/>)
/// and whose C++ type has a default constructor: an array alias (<c>GSharpArray&lt;T&gt;</c>) or such a struct.
/// </summary>
/// <remarks>
/// [WHY] A declaration that a goto jumps past is split into <c>&lt;type&gt; &lt;name&gt;;</c> and an assignment (#768). C++
/// allows a jump past <c>&lt;type&gt; &lt;name&gt;;</c> only when that default initialization does nothing (a scalar or a
/// trivial struct, [stmt.dcl]). A struct with an array field holds a <c>GSharpArray&lt;T&gt;</c>, and a struct with an
/// implicit conversion operator gets <c>T() {}</c> (StructTranspiler): both have a default constructor, so g++ and clang
/// reject the jump (MSVC accepts it). Such a local is declared before the first statement of its list, where no goto of
/// the list jumps past it, and its declaration becomes an assignment. C# makes sure that it is assigned before it is read,
/// so the earlier default construction is not observable.
/// </remarks>
internal static class JumpedOverDeclarations
{
    /// <summary>Whether <paramref name="declarator"/> is declared at the start of its statement list.</summary>
    /// <remarks>A local that the block around its switch declares (<see cref="SwitchSectionLocals"/>) is not declared again.</remarks>
    public static bool IsDeclaredAtListStart(VariableDeclaratorSyntax declarator, SemanticModel semanticModel) =>
        declarator.Parent is VariableDeclarationSyntax
        {
            Type: not RefTypeSyntax,
            Parent: LocalDeclarationStatementSyntax { UsingKeyword.RawKind: (int)SyntaxKind.None, IsConst: false } statement
        } &&
        IsArrayOrHasCppDefaultConstructor(declarator, statement, semanticModel) &&
        GotoJumpAnalysis.MayBeJumpedOver(statement) &&
        !SwitchSectionLocals.IsDeclaredBeforeSwitch(declarator, semanticModel);

    // An array alias (int[] a = input, a Span<T> over an array) is a GSharpArray<T>, which has a default constructor; a
    // stack array (new T[N], stackalloc) is a C++ array that keeps its own form (zero fill, VariableDeclarationTranspilerRule)
    private static bool IsArrayOrHasCppDefaultConstructor(VariableDeclaratorSyntax declarator,
        LocalDeclarationStatementSyntax statement, SemanticModel semanticModel)
    {
        var type = semanticModel.GetTypeSymbol(statement.Declaration.Type);
        if (!type.IsArrayLikeType())
        {
            return HasCppDefaultConstructor(type);
        }

        return semanticModel.GetDeclaredSymbol<ISymbol>(declarator) is ILocalSymbol local &&
               !StackArrayLocals.IsStackArray(local, out _);
    }

    /// <summary>Writes <c>&lt;type&gt; &lt;name&gt;;</c> for each local of <paramref name="statements"/> declared at its start.</summary>
    public static void WriteDeclarations(SyntaxList<StatementSyntax> statements, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        foreach (var statement in statements)
        {
            if (statement is not LocalDeclarationStatementSyntax declaration)
            {
                continue;
            }

            foreach (var declarator in declaration.Declaration.Variables)
            {
                if (!IsDeclaredAtListStart(declarator, model))
                {
                    continue;
                }

                // <type> <name>;
                context.Emitter.Dialect.AppendType(ref context, model.GetTypeSymbol(declaration.Declaration.Type));
                context.Emitter.Writer.Append(' ');
                CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
                context.Emitter.Writer.AppendLine(";");
            }
        }
    }

    // A struct whose C++ form has a default constructor: it holds an array (GSharpArray<T>) or such a struct
    private static bool HasCppDefaultConstructor(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRefLikeType: false, SpecialType: SpecialType.None } named &&
        named
            .GetMembers()
            .AsStackEnumerable()
            .Any(static member => member is IFieldSymbol { IsStatic: false, IsConst: false } field &&
                                  (field.Type.IsArrayLikeType() || HasCppDefaultConstructor(field.Type)));
}
