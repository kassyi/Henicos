namespace Kassyi.GSharp.Compiler.Transpiler.Rules.DeclarationRules;

/// <summary>
/// Transpiles C# local declaration statements into target dialect variable definitions,
/// with special handling for static and dynamic GPU shared memory declarations (<c>SharedMemory&lt;T&gt;</c>).
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and safe for concurrent execution across threads.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class LocalDeclarationStatementTranspilerRule : SyntaxNodeTranspilerRuleBase<LocalDeclarationStatementSyntax>
{
    /// <inheritdoc />
    public override void Transpile(LocalDeclarationStatementSyntax node, ref TranspileContext context)
    {
        var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node.Declaration.Type);
        if (typeSymbol.IsSharedMemoryType())
        {
            ProcessSharedMemoryDeclaration(node, (INamedTypeSymbol)typeSymbol, ref context);
            return;
        }

        if (node.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
        {
            UsingTranspiler.TranspileUsingDeclaration(node, ref context);
            return;
        }

        StatementPrelude.Emit(node, ref context);

        var lengthBefore = context.Emitter.Writer.Length;
        // Dispatch inner variable declaration transpilation to the visitor (which will use VariableDeclarationTranspilerRule)
        context.Visitor.Visit(node.Declaration);
        if (context.Emitter.Writer.Length > lengthBefore)
        {
            context.Emitter.Writer.Append(';');
        }
    }

    private static void ProcessSharedMemoryDeclaration(LocalDeclarationStatementSyntax node,
        INamedTypeSymbol typeSymbol, ref TranspileContext context)
    {
        var typeArg = typeSymbol.TypeArguments[0];

        for (var i = 0; i < node.Declaration.Variables.Count; i++)
        {
            var variable = node.Declaration.Variables[i];
            if (DynamicSharedMemoryAnalysis.GetCreation(variable) is not { } creation)
            {
                // A copy of another local or the result of a call is a handle to memory that is already declared
                if (variable.Initializer is { } initializer)
                {
                    WriteSharedMemoryHandle(variable, initializer, typeArg, node, ref context);
                    AppendLineBetween(node, i, ref context);
                }

                continue;
            }

            var lengthBefore = context.Emitter.Writer.Length;
            SharedMemoryDeclarationWriter.Write(SharedMemoryName.Of(variable), creation, typeArg,
                GotoJumpAnalysis.MayBeJumpedOver(node), ref context);
            if (context.Emitter.Writer.Length > lengthBefore)
            {
                AppendLineBetween(node, i, ref context);
            }
        }
    }

    private static void AppendLineBetween(LocalDeclarationStatementSyntax node, int index, ref TranspileContext context)
    {
        if (index < node.Declaration.Variables.Count - 1)
        {
            context.Emitter.Writer.AppendLine();
        }
    }

    // <T>* <name> = <initializer>;
    // A goto may jump past the declaration, and C++ forbids that jump past an initialization:
    // <T>* <name>;
    // <name> = <initializer>;
    private static void WriteSharedMemoryHandle(VariableDeclaratorSyntax declarator,
        EqualsValueClauseSyntax initializer, ITypeSymbol elementType, LocalDeclarationStatementSyntax node,
        ref TranspileContext context)
    {
        // The memory of a creation inside the initializer ('c ? new ... : new ...', a call argument) is declared first
        StatementPrelude.Emit(initializer, ref context);
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.Append("* ");
        CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
        if (GotoJumpAnalysis.MayBeJumpedOver(node))
        {
            context.Emitter.Writer.AppendLine(";");
            CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
        }

        context.Emitter.Writer.Append(" = ");
        context.Visitor.Visit(initializer.Value);
        context.Emitter.Writer.Append(';');
    }
}
