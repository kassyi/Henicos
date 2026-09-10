namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class LambdaExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<AnonymousFunctionExpressionSyntax>
{
    public static readonly LambdaExpressionTranspilerRule Instance = new();

    private LambdaExpressionTranspilerRule()
    {
    }

    public override void Transpile(AnonymousFunctionExpressionSyntax node, ref TranspileContext context)
    {
        switch (node)
        {
            case SimpleLambdaExpressionSyntax simpleLambda:
                TranspileLambda(simpleLambda.Parameter, simpleLambda.Block, simpleLambda.ExpressionBody, ref context);
                break;
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                TranspileLambda(parenthesizedLambda.ParameterList, parenthesizedLambda.Block,
                    parenthesizedLambda.ExpressionBody, ref context);
                break;
            case AnonymousMethodExpressionSyntax anonymousMethod:
                TranspileLambda(anonymousMethod.ParameterList, anonymousMethod.Block, anonymousMethod.ExpressionBody,
                    ref context);
                break;
        }
    }

    private static void TranspileLambda(
        SyntaxNode? parameterOrList,
        BlockSyntax? block,
        ExpressionSyntax? expressionBody,
        ref TranspileContext context)
    {
        context.Writer.Append("[&]");

        if (parameterOrList != null)
        {
            if (parameterOrList is ParameterSyntax parameter)
            {
                // Simple lambda has no parentheses around its single parameter
                using (context.Writer.Parentheses())
                {
                    context.Visitor.Visit(parameter);
                }
            }
            else
            {
                context.Visitor.Visit(parameterOrList);
            }
        }
        else
        {
            context.Writer.AppendParentheses();
        }

        context.Writer.AppendSpace();

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
    }
}
