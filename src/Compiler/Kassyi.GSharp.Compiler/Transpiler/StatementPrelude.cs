namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes the declarations that a statement needs before it, which C++ cannot write inside an expression: the variables
/// of inline declaration expressions (<c>out var x</c>, <c>is T x</c>) and of deconstruction declarations
/// (<c>var (a, (b, c)) = ...</c>), the storages of the <c>stackalloc</c> and collection expressions outside a stack
/// array declaration (<see cref="StackAllocEmitter"/>), and the variables of discard <c>out</c> arguments
/// (<see cref="DiscardOutArgument"/>).
/// </summary>
/// <remarks>
/// The walk does not enter a block (the body of a lambda), whose statements write their own prelude; it enters an
/// expression-bodied lambda. Only the declarations are written here: a storage is zeroed or filled where its expression
/// is evaluated, so that each evaluation (in a loop condition, for example) starts from a new block as in C#, and so that
/// the elements of an initializer keep their C# order among the other operands.
/// </remarks>
internal static class StatementPrelude
{
    public static void Emit(SyntaxNode node, ref TranspileContext context)
    {
        foreach (var desc in node.DescendantNodes(n => n == node || n is not BlockSyntax))
        {
            if (desc is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax singleVar } declExpr)
            {
                // [WHY] The C++ dialect transpiles lowered syntax trees. When a lowered declaration expression (such as
                // an inlined 'out var x') loses its original-node annotation, GetTypeSymbol cannot locate the node in
                // the semantic model and returns null. Instead of silently emitting invalid C++ ("void x;" or the former
                // fallback "auto x;"), throw an internal error so the generator reports GSH0048 for the containing method.
                var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(declExpr)
                    ?? throw new InvalidOperationException(
                        $"The type of declaration expression '{declExpr}' could not be resolved from the semantic model.");

                context.Emitter.Dialect.AppendType(ref context, typeSymbol);
                context.Emitter.Writer.Append(' ');
                CppNameResolver.AppendDeclaredName(singleVar, singleVar.Identifier, ref context);
                context.Emitter.Writer.AppendLine(";");
                continue;
            }

            // A variable of var (a, (b, c)) = ...: the declaration expression has the tuple type, each variable its own
            if (desc is SingleVariableDesignationSyntax { Parent: ParenthesizedVariableDesignationSyntax } variable &&
                IsDeconstructionDeclaration(variable))
            {
                var local = context.Query.SemanticModel.GetDeclaredSymbol<ILocalSymbol>(variable)
                    ?? throw new InvalidOperationException(
                        $"The variable '{variable}' of a deconstruction could not be resolved from the semantic model.");

                context.Emitter.Dialect.AppendType(ref context, local.Type);
                context.Emitter.Writer.Append(' ');
                CppNameResolver.AppendDeclaredName(variable, variable.Identifier, ref context);
                context.Emitter.Writer.AppendLine(";");
                continue;
            }

            StackAllocEmitter.DeclareStorage(desc, ref context);
            DiscardOutArgument.DeclareStorage(desc, ref context);
        }
    }

    // The designation belongs to the left side of a deconstruction assignment (not a foreach or a pattern)
    private static bool IsDeconstructionDeclaration(SyntaxNode designation)
    {
        var node = designation.Parent;
        while (node is ParenthesizedVariableDesignationSyntax)
        {
            node = node.Parent;
        }

        return node is DeclarationExpressionSyntax { Parent: AssignmentExpressionSyntax assignment } declaration &&
               assignment.Left == declaration;
    }
}
