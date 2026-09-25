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
        try
        {
            if (block != null)
            {
                using (context.Writer.Scope())
                {
                    for (var i = 0; i < block.Statements.Count; i++)
                    {
                        var stmt = block.Statements[i];
                        context.Visitor.Visit(stmt);
                        context.Writer.AppendLine();
                        if (context.Visitor.ShouldInsertAutoSync(block.Statements, i))
                        {
                            context.Writer.AppendLine("cg::this_grid().sync();");
                        }
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
        }
        finally
        {
            context.CallableDepth--;
        }
    }
}
