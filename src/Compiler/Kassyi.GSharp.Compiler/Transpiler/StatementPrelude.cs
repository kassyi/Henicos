namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes the declarations that a statement needs before it, which C++ cannot write inside an expression: the variables
/// of inline declaration expressions (<c>out var x</c>, <c>is T x</c>) and the storages of the <c>stackalloc</c> and
/// collection expressions outside a stack array declaration (<see cref="StackAllocEmitter"/>).
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
                var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(declExpr);
                if (typeSymbol != null)
                {
                    context.Emitter.Dialect.AppendType(ref context, typeSymbol);
                }
                else
                {
                    context.Emitter.Writer.Append("auto");
                }

                context.Emitter.Writer.Append(' ');
                CppNameResolver.AppendDeclaredName(singleVar, singleVar.Identifier, ref context);
                context.Emitter.Writer.AppendLine(";");
                continue;
            }

            StackAllocEmitter.DeclareStorage(desc, ref context);
        }
    }
}
