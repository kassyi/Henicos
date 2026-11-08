using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles postfix unary expressions (<c>x++</c>, <c>x--</c>, <c>x!</c>) into target dialect syntax.
/// </summary>
/// <remarks>
/// <para>
/// The null-forgiving operator <c>x!</c> only tells the C# compiler that <c>x</c> is not null; it has no run-time
/// meaning (C# spec §12.8.9), so only the operand is written (#880).
/// </para>
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
        if (node.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            context.Visitor.Visit(node.Operand);
            return;
        }

        if ((node.IsKind(SyntaxKind.PostIncrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression)) &&
            ComputedPropertyGetterCall.IsComputedPropertyTarget(node.Operand, ref context))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node,
                ComputedPropertyGetterCall.IncrementOrDecrementOfComputedProperty);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        // Narrow, int and long ++ / -- wrap in C#; C++ leaves signed overflow undefined and nvcc can keep the
        // unnarrowed value of a raw narrow step (#722, #1079)
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
