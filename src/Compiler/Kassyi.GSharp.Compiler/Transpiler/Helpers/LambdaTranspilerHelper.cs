namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Provides shared logic for transpiling C# anonymous functions and local functions into C++ capture-by-reference lambdas.
/// </summary>
internal static class LambdaTranspilerHelper
{
    public static void TranspileLambdaBody(
        ref TranspileContext context,
        BlockSyntax? block,
        ExpressionSyntax? expressionBody,
        bool isVoidExpression = false)
    {
        context.Emitter.CallableDepth++;
        try
        {
            if (block != null)
            {
                context.Visitor.VisitStatementOrBlock(block);
            }
            else if (expressionBody != null)
            {
                using (context.Emitter.Writer.Scope())
                {
                    context.Emitter.Writer.AppendIf(!isVoidExpression, "return ");
                    context.Visitor.Visit(expressionBody);
                    context.Emitter.Writer.AppendLine(";");
                }
            }
        }
        finally
        {
            context.Emitter.CallableDepth--;
        }
    }
}
