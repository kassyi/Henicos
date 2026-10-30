using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles prefix unary expressions (<c>++x</c>, <c>--x</c>, <c>!x</c>, <c>~x</c>, <c>-x</c>, <c>+x</c>, and pointer indirection <c>*ptr</c>) into target dialect syntax.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class PrefixUnaryExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<PrefixUnaryExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="PrefixUnaryExpressionTranspilerRule"/>.
    /// </summary>
    public PrefixUnaryExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(PrefixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        // For pointers: *a
        if (node.IsKind(SyntaxKind.PointerIndirectionExpression))
        {
            context.Emitter.Writer.Append("*");
            context.Visitor.Visit(node.Operand);
            return;
        }

        if ((node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PreDecrementExpression)) &&
            ComputedPropertyGetterCall.IsComputedPropertyTarget(node.Operand, ref context))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node,
                ComputedPropertyGetterCall.IncrementOrDecrementOfComputedProperty);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        // -2147483648 is int.MinValue in C#, but C++ negates the literal 2147483648, which does not fit int
        if (OperatorSpacing.IsNegatedNumericLiteral(node) &&
            context.Query.SemanticModel.GetSafeConstantValue(node) is { HasValue: true, Value: { } value } &&
            context.Query.SemanticModel.GetTypeSymbol(node) is { } type)
        {
            ConstantAtUse.Append(node, type, value, ref context);
            return;
        }

        // -u negates a long in C#, an unsigned int in C++ (#671). The converted operand begins with '(' or is a literal,
        // so it cannot merge with the operator
        if (node.IsKind(
                SyntaxKind.UnaryMinusExpression, SyntaxKind.UnaryPlusExpression, SyntaxKind.BitwiseNotExpression) &&
            context.Query.SemanticModel.GetSafeOperation(node) is IUnaryOperation operation &&
            operation.Syntax.RawKind == node.RawKind &&
            ArithmeticConversions.TryGetPromotedType(operation, out var promoted))
        {
            context.Emitter.Writer.Append(node.OperatorToken.Text);
            ArithmeticConversions.AppendOperand(operation.Operand, node.Operand, promoted, ref context);
            return;
        }

        OperatorSpacing.AppendOperator(node.OperatorToken.Text, node.Operand, ref context);
        context.Visitor.Visit(node.Operand);
    }
}
