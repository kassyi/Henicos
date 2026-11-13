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

        // A conditional of a Nullable<T> type whose branch is a T or null (c ? v : null): C++ has no common type of T and
        // nullptr, so a branch of another type is converted to the nullable type, as C# converts it (#1143)
        // (the type of a target-typed conditional is the type it converts to)
        if ((context.Query.SemanticModel.GetTypeSymbol(node) ??
             context.Query.SemanticModel.GetConvertedTypeSymbol(node)) is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable &&
            (NeedsNullableConversion(node.WhenTrue, nullable, ref context) ||
             NeedsNullableConversion(node.WhenFalse, nullable, ref context)))
        {
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(node.Condition);
                context.Emitter.Writer.Append('?');
                AppendAsNullable(node.WhenTrue, nullable, ref context);
                context.Emitter.Writer.Append(':');
                AppendAsNullable(node.WhenFalse, nullable, ref context);
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

        TranspileBranches(node, ref context);
    }

    // The branch is null, default or a value of another type than the nullable type of the conditional
    private static bool NeedsNullableConversion(ExpressionSyntax branch, ITypeSymbol nullable,
        ref TranspileContext context) =>
        IsNullOrDefault(branch) || !nullable.IsEqualTo(context.Query.SemanticModel.GetTypeSymbol(branch));

    private static bool IsNullOrDefault(ExpressionSyntax branch) =>
        IntegerSemanticsEmitter.Unparenthesize(branch)
            .IsKind(SyntaxKind.NullLiteralExpression, SyntaxKind.DefaultLiteralExpression);

    // <branch> of the nullable type; <T?>() for null or default, <T?>(<branch>) for a value of another type
    private static void AppendAsNullable(ExpressionSyntax branch, ITypeSymbol nullable, ref TranspileContext context)
    {
        if (!NeedsNullableConversion(branch, nullable, ref context))
        {
            context.Visitor.Visit(branch);
            return;
        }

        context.Emitter.Dialect.AppendType(ref context, nullable);
        using (context.Emitter.Writer.Parentheses())
        {
            if (!IsNullOrDefault(branch))
            {
                context.Visitor.Visit(branch);
            }
        }
    }

    // A stack array local is a C++ array. The C++ conditional of it and a kernel array (a GSharpArray<T>), or of two C
    // arrays of different sizes, decays the array to a pointer, which an array parameter does not take: each branch is
    // then written as the span it converts to. Two C arrays of one size are a C++ array lvalue as they are
    private static bool NeedsViews(ConditionalExpressionSyntax node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var whenTrue = StackArrayLocals.IsStackArrayReference(node.WhenTrue, model);
        var whenFalse = StackArrayLocals.IsStackArrayReference(node.WhenFalse, model);
        if (!whenTrue || !whenFalse)
        {
            return whenTrue || whenFalse;
        }

        return !StackArrayLocals.TryGetLength(node.WhenTrue, model, out var first) ||
               !StackArrayLocals.TryGetLength(node.WhenFalse, model, out var second) ||
               first != second;
    }

    private static void AppendBranch(ExpressionSyntax branch, bool asView, ref TranspileContext context)
    {
        if (asView)
        {
            StackArrayLocals.AppendView(branch, ref context);
        }
        else
        {
            context.Visitor.Visit(branch);
        }
    }

    private static void TranspileBranches(ConditionalExpressionSyntax node, ref TranspileContext context)
    {
        var asViews = NeedsViews(node, ref context);
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
                AppendBranch(node.WhenTrue, asViews, ref context);
            }

            context.Emitter.Writer.Append(':');

            if (node.WhenFalse is RefExpressionSyntax refFalse)
            {
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, refFalse.Expression);
            }
            else
            {
                AppendBranch(node.WhenFalse, asViews, ref context);
            }
        }
    }
}
