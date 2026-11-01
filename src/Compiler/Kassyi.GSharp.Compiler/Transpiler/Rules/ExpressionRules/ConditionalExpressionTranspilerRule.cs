using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;/// <summary>
/// Transpiles C# ternary conditional expressions (<c>condition ? trueExpr : falseExpr</c>) into parenthesized C++ conditional expressions.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ConditionalExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ConditionalExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="ConditionalExpressionTranspilerRule"/>.
    /// </summary>
    public ConditionalExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(ConditionalExpressionSyntax node, ref TranspileContext context)
    {
        if (node.WhenTrue is RefExpressionSyntax || node.WhenFalse is RefExpressionSyntax)
        {
            // [WHY] A ref conditional is an lvalue in C#. Selecting between addresses and dereferencing the result
            // yields an equivalent C++ lvalue, so it works both as a value and as a 'ref' argument ('&(*(...))').
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Writer.Append('*');
                TranspileBranches(node, ref context);
            }

            return;
        }

        // A target-typed c ? u : n converts both branches to its type in C#; C++ gives it unsigned int (#671)
        if (context.Query.SemanticModel.GetSafeOperation(node) is
                IConditionalOperation { WhenFalse: { } whenFalse } operation &&
            operation.Syntax.RawKind == node.RawKind &&
            ArithmeticConversions.TryGetPromotedType(operation, out var type))
        {
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(node.Condition);
                context.Emitter.Writer.Append('?');
                ArithmeticConversions.AppendOperand(operation.WhenTrue, node.WhenTrue, type, ref context);
                context.Emitter.Writer.Append(':');
                ArithmeticConversions.AppendOperand(whenFalse, node.WhenFalse, type, ref context);
            }

            return;
        }

        if (context.Query.SemanticModel.GetSafeOperation(node) is
                IConditionalOperation { WhenFalse: { } falseBranch, Type: { } branchTargetType } conditional &&
            conditional.Syntax.RawKind == node.RawKind &&
            (IsUserDefinedConversion(conditional.WhenTrue) || IsUserDefinedConversion(falseBranch)))
        {
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(node.Condition);
                context.Emitter.Writer.Append('?');
                TranspileBranchWithUserConversion(conditional.WhenTrue, node.WhenTrue, branchTargetType, ref context);
                context.Emitter.Writer.Append(':');
                TranspileBranchWithUserConversion(falseBranch, node.WhenFalse, branchTargetType, ref context);
            }

            return;
        }

        TranspileBranches(node, ref context);
    }

    private static bool IsUserDefinedConversion(IOperation branch) =>
        branch is IConversionOperation { Conversion.IsUserDefined: true };

    private static void TranspileBranchWithUserConversion(IOperation branch, ExpressionSyntax syntax,
        ITypeSymbol targetType, ref TranspileContext context)
    {
        if (IsUserDefinedConversion(branch))
        {
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, targetType);
            }

            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(syntax);
            }

            return;
        }

        context.Visitor.Visit(syntax);
    }

    private static void TranspileBranches(ConditionalExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
            context.Emitter.Writer.Append('?');

            // Handle 'ref' conditionals if present
            if (node.WhenTrue is RefExpressionSyntax refTrue)
            {
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, refTrue.Expression);
            }
            else
            {
                context.Visitor.Visit(node.WhenTrue);
            }

            context.Emitter.Writer.Append(':');

            if (node.WhenFalse is RefExpressionSyntax refFalse)
            {
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, refFalse.Expression);
            }
            else
            {
                context.Visitor.Visit(node.WhenFalse);
            }
        }
    }
}
