namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

internal static class DeclarationTranspilerHelper
{
    public static void TranspileCombinedDeclarations(
        SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol,
        bool isVar,
        bool isPointer,
        ref TranspileContext context)
    {
        if (isVar)
        {
            context.Emitter.Writer.Append("auto");
        }
        else
        {
            if (isPointer && typeSymbol is IPointerTypeSymbol pointerType)
            {
                context.Emitter.Dialect.AppendType(ref context, pointerType.PointedAtType);
            }
            else
            {
                context.Emitter.Dialect.AppendType(ref context, typeSymbol);
            }
        }
        context.Emitter.Writer.Append(" ");

        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            if (isPointer)
            {
                context.Emitter.Writer.Append('*');
            }

            CppNameResolver.AppendDeclaredName(v, v.Identifier, ref context);

            WriteDeclaratorInitializer(v, ref context);

            if (i < variables.Count - 1)
            {
                context.Emitter.Writer.AppendComma();
            }
        }
    }

    public static void WriteDeclaratorInitializer(VariableDeclaratorSyntax variable, ref TranspileContext context)
    {
        if (variable.Initializer != null)
        {
            if (context.Emitter.SkipLocalsInit)
            {
                var val = variable.Initializer.Value;
                if (val is ImplicitObjectCreationExpressionSyntax or ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 } or DefaultExpressionSyntax ||
                    val is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.DefaultLiteralExpression))
                {
                    return; // Skip zero-initialization
                }
            }

            context.Emitter.Writer.Append(" = ");
            context.Visitor.Visit(variable.Initializer.Value);
        }
    }
}
