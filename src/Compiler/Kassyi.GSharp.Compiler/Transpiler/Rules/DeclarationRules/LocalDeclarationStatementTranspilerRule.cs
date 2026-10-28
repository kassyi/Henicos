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

        StatementPrelude.Emit(node, ref context);

        var lengthBefore = context.Emitter.Writer.Length;
        // Dispatch inner variable declaration transpilation to the visitor (which will use VariableDeclarationTranspilerRule)
        context.Visitor.Visit(node.Declaration);
        if (context.Emitter.Writer.Length > lengthBefore)
        {
            context.Emitter.Writer.Append(';');
        }

        if (node.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
        {
            UsingTranspiler.TranspileUsingDeclaration(node, ref context);
        }
    }

    private static void ProcessSharedMemoryDeclaration(LocalDeclarationStatementSyntax node,
        INamedTypeSymbol typeSymbol, ref TranspileContext context)
    {
        var typeArg = typeSymbol.TypeArguments[0];

        for (var i = 0; i < node.Declaration.Variables.Count; i++)
        {
            var variable = node.Declaration.Variables[i];
            if (!context.Emitter.Dialect.IsCuda)
            {
                if (variable.Initializer?.Value is BaseObjectCreationExpressionSyntax creation)
                {
                    WriteCpuSharedMemory(variable, creation, typeArg, node, i, ref context);
                }

                continue;
            }

            if (!DynamicSharedMemoryAnalysis.TryGetCount(variable, context.Query.SemanticModel, out var cudaCreation,
                    out var count, out var constantCount))
            {
                continue;
            }

            if (constantCount == null)
            {
                DynamicSharedMemoryEmitter.WritePlacement(variable, cudaCreation, count, typeArg, ref context);
            }
            else
            {
                WriteStaticSharedMemory(variable, count, typeArg, ref context);
            }

            if (i < node.Declaration.Variables.Count - 1)
            {
                context.Emitter.Writer.AppendLine();
            }
        }
    }

    private static void WriteCpuSharedMemory(
        VariableDeclaratorSyntax declarator,
        BaseObjectCreationExpressionSyntax creation,
        ITypeSymbol typeArg,
        LocalDeclarationStatementSyntax node,
        int index,
        ref TranspileContext context)
    {
        context.Emitter.Dialect.AppendType(ref context, typeArg);
        context.Emitter.Writer.Append("* ");
        CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
        context.Emitter.Writer.Append(" = ");
        SharedMemorySiteKey.AppendCpuAllocation(creation, typeArg, ref context);
        context.Emitter.Writer.Append(';');
        if (index < node.Declaration.Variables.Count - 1)
        {
            context.Emitter.Writer.AppendLine();
        }
    }

    // __shared__ <T> <name>[<n>];
    private static void WriteStaticSharedMemory(VariableDeclaratorSyntax declarator, ExpressionSyntax count,
        ITypeSymbol typeArg, ref TranspileContext context)
    {
        context.Emitter.Writer.Append("__shared__ ");
        context.Emitter.Dialect.AppendType(ref context, typeArg);
        context.Emitter.Writer.Append(' ');
        CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
        using (context.Emitter.Writer.Brackets())
        {
            context.Visitor.Visit(count);
        }

        context.Emitter.Writer.Append(";");
    }
}
