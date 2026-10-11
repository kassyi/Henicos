
namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;/// <summary>
/// Transpiles anonymous function syntax nodes (simple lambdas, parenthesized lambdas, and anonymous methods)
/// into target dialect C++ capture-by-reference lambdas (<c>[&amp;](...) { ... }</c>).
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class LambdaExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<AnonymousFunctionExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="LambdaExpressionTranspilerRule"/>.
    /// </summary>
    public LambdaExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
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
        context.Emitter.Writer.Append("[&]");

        if (parameterOrList != null)
        {
            if (parameterOrList is ParameterSyntax parameter)
            {
                // Simple lambda has no parentheses around its single parameter
                using (context.Emitter.Writer.Parentheses())
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
            context.Emitter.Writer.AppendParentheses();
        }

        context.Emitter.Writer.AppendSpace();

        LambdaTranspilerHelper.TranspileLambdaBody(ref context, block, expressionBody);
    }
}
