using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles postfix unary expressions (<c>x++</c>, <c>x--</c>) into target dialect syntax.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class PostfixUnaryExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<PostfixUnaryExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="PostfixUnaryExpressionTranspilerRule"/>.
    /// </summary>
    public PostfixUnaryExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(PostfixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        if ((node.IsKind(SyntaxKind.PostIncrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression)) &&
            ComputedPropertyGetterCall.IsComputedPropertyTarget(node.Operand, ref context))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node,
                ComputedPropertyGetterCall.IncrementOrDecrementOfComputedProperty);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        // int and long ++ / -- wrap in C#; C++ leaves the overflow undefined (#722)
        if (context.Query.SemanticModel.GetSafeOperation(node) is IIncrementOrDecrementOperation step &&
            step.Syntax.RawKind == node.RawKind &&
            IntegerSemantics.GetIncrementHelper(step, node, ref context) is { } helper)
        {
            IntegerSemanticsEmitter.AppendStep(step, node, node.Operand, helper, ref context);
            return;
        }

        context.Visitor.Visit(node.Operand);
        context.Emitter.Writer.Append(node.OperatorToken.Text);
    }
}
