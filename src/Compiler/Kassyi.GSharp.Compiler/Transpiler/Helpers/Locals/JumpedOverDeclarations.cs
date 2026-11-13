namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>
/// Declares at the start of their C++ scope (<see cref="CppBlockScopes"/>) the locals that a <c>goto</c> jumps past
/// (<see cref="GotoJumpAnalysis"/>) and whose C++ type has a default constructor: an array alias
/// (<c>GSharpArray&lt;T&gt;</c>) or such a struct.
/// </summary>
/// <remarks>
/// [WHY] A declaration that a goto jumps past is split into <c>&lt;type&gt; &lt;name&gt;;</c> and an assignment (#768). C++
/// allows a jump past <c>&lt;type&gt; &lt;name&gt;;</c> only when that default initialization does nothing (a scalar or a
/// trivial struct, [stmt.dcl]). A struct with an array field holds a <c>GSharpArray&lt;T&gt;</c>, and a struct with an
/// implicit conversion operator gets <c>T() {}</c> (StructConversionEmitter): both have a default constructor, so g++
/// and clang reject the jump (MSVC accepts it). Such a local is declared before the first statement of its C++ scope,
/// where no goto of the scope jumps past it, and its declaration becomes an assignment; a local of a nested block
/// written without braces is declared at the start of the scope around it. C# makes sure that it is assigned before it
/// is read, so the earlier default construction is not observable. A goto from another switch section enters the
/// section past its start, so such a local is declared before the switch instead (<see cref="SwitchSectionLocals"/>).
/// </remarks>
internal static class JumpedOverDeclarations
{
    /// <summary>Whether <paramref name="declarator"/> is declared at the start of its C++ scope.</summary>
    /// <remarks>A local that the block around its switch declares (<see cref="SwitchSectionLocals"/>) is not declared again.</remarks>
    public static bool IsDeclaredAtScopeStart(VariableDeclaratorSyntax declarator, SemanticModel semanticModel) =>
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

    /// <summary>
    /// Writes <c>&lt;type&gt; &lt;name&gt;;</c> for each local of the C++ scope that starts with
    /// <paramref name="statements"/> declared at its start: the locals of the list, under its labels and in its nested
    /// blocks written without braces.
    /// </summary>
    public static void WriteDeclarations(SyntaxList<StatementSyntax> statements, ref TranspileContext context)
    {
        foreach (var statement in statements)
        {
            var unlabeled = statement;
            while (unlabeled is LabeledStatementSyntax labeled)
            {
                unlabeled = labeled.Statement;
            }

            if (unlabeled is BlockSyntax nested && CppBlockScopes.IsWrittenWithoutBraces(nested))
            {
                WriteDeclarations(nested.Statements, ref context);
            }
            else if (unlabeled is LocalDeclarationStatementSyntax declaration)
            {
                WriteDeclarations(declaration, ref context);
            }
        }
    }

    private static void WriteDeclarations(LocalDeclarationStatementSyntax declaration, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        foreach (var declarator in declaration.Declaration.Variables)
        {
            if (!IsDeclaredAtScopeStart(declarator, model))
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

    // A struct whose C++ form has a default constructor: it holds an array (GSharpArray<T>) or such a struct
    private static bool HasCppDefaultConstructor(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRefLikeType: false, SpecialType: SpecialType.None } named &&
        named
            .GetMembers()
            .AsStackEnumerable()
            .Any(static member => member is IFieldSymbol { IsStatic: false, IsConst: false } field &&
                                  (field.Type.IsArrayLikeType() || HasCppDefaultConstructor(field.Type)));
}
