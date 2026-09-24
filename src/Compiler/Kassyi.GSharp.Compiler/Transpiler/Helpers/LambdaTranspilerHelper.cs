using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Provides shared logic for transpiling C# anonymous functions and local functions into C++ capture-by-reference lambdas.
/// </summary>
internal static class LambdaTranspilerHelper
{
    public static void TranspileLambdaBody(
        ref TranspileContext context,
        BlockSyntax? block,
        ExpressionSyntax? expressionBody)
    {
        context.CallableDepth++;
        if (block != null)
        {
            using (context.Writer.Scope())
            {
                foreach (var stmt in block.Statements)
                {
                    context.Visitor.Visit(stmt);
                    context.Writer.AppendLine();
                }
            }
        }
        else if (expressionBody != null)
        {
            using (context.Writer.Scope())
            {
                context.Writer.Append("return ");
                context.Visitor.Visit(expressionBody);
                context.Writer.AppendSemiColonLine();
            }
        }
        context.CallableDepth--;
    }
}
