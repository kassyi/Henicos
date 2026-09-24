using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
            context.Writer.Append("auto");
        }
        else
        {
            if (isPointer && typeSymbol is IPointerTypeSymbol pointerType)
            {
                context.AppendType(pointerType.PointedAtType);
            }
            else
            {
                context.AppendType(typeSymbol);
            }
        }
        context.Writer.Append(" ");

        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            if (isPointer)
            {
                context.Writer.Append($"*{v.Identifier.Text}");
            }
            else
            {
                context.Writer.Append(v.Identifier.Text);
            }

            WriteDeclaratorInitializer(v, ref context);

            if (i < variables.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }

    public static void WriteDeclaratorInitializer(VariableDeclaratorSyntax variable, ref TranspileContext context)
    {
        if (variable.Initializer != null)
        {
            if (context.SkipLocalsInit)
            {
                var val = variable.Initializer.Value;
                if (val is ImplicitObjectCreationExpressionSyntax or ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 } or DefaultExpressionSyntax ||
                    val is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.DefaultLiteralExpression))
                {
                    return; // Skip zero-initialization
                }
            }

            context.Writer.Append(" = ");
            context.Visitor.Visit(variable.Initializer.Value);
        }
    }
}
