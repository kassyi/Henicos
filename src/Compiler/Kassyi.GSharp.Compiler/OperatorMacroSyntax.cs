using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Reads an operator macro expression: the operator call inside parentheses and transparent casts, the user-defined
/// operator it calls, its <c>[GpuOperatorMacro]</c>, its operator text and its operands.
/// </summary>
internal static class OperatorMacroSyntax
{
    // The operator call inside the parentheses and the transparent casts written around it: c = (T)(a + b) calls +
    // (#641). A cast with a user-defined conversion stops it: the expansion passes the operator's own result on, so
    // the conversion would be dropped (#759); OperatorMacroPosition.GetPositionFailure reports the operator inside
    internal static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case CastExpressionSyntax cast when IsTransparentCast(cast, semanticModel):
                    expression = cast.Expression;
                    break;
                default:
                    return expression;
            }
        }
    }

    // A cast whose conversion is not user-defined (an identity or built-in conversion), which the expansion can skip
    internal static bool IsTransparentCast(CastExpressionSyntax cast, SemanticModel semanticModel) =>
        semanticModel.GetSafeOperation(cast) is not IConversionOperation { Conversion.IsUserDefined: true };

    // The user-defined operator that a binary, unary, increment or decrement expression calls, whether it is lifted to
    // nullable operands, whether it is an increment or decrement, and for a user-defined && or || the operator false or
    // true that tests its left operand (Roslyn gives the & or | as the operator method)
    internal static (IMethodSymbol? Method, bool IsLifted, bool IsIncrementOrDecrement, IMethodSymbol? Test)
        GetOperator(ExpressionSyntax operatorCall, SemanticModel semanticModel)
    {
        if (operatorCall is not (BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax))
        {
            return default;
        }

        var operation = semanticModel.GetSafeOperation(operatorCall);
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation switch
        {
            IBinaryOperation
                {
                    OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr
                } binary =>
                (binary.OperatorMethod, binary.IsLifted, false, FindTest(binary)),
            IBinaryOperation binary => (binary.OperatorMethod, binary.IsLifted, false, null),
            IUnaryOperation unary => (unary.OperatorMethod, unary.IsLifted, false, null),
            IIncrementOrDecrementOperation incrementOrDecrement =>
                (incrementOrDecrement.OperatorMethod, incrementOrDecrement.IsLifted, true, null),
            _ => default
        };
    }

    // T.false of x && y and T.true of x || y, T being the type that declares the & or | (C# spec §12.14.3): the
    // operator of T that takes T
    private static IMethodSymbol? FindTest(IBinaryOperation conditional)
    {
        if (conditional.OperatorMethod is not { ContainingType: { } type, ReturnType: var operandType })
        {
            return null;
        }

        var name = conditional.OperatorKind == BinaryOperatorKind.ConditionalAnd
            ? WellKnownMemberNames.FalseOperatorName
            : WellKnownMemberNames.TrueOperatorName;
        return type.GetMembers(name)
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(operandType, static (test, t) =>
                test is { MethodKind: MethodKind.UserDefinedOperator, Parameters: [var parameter] } &&
                parameter.Type.IsEqualTo(t)
            );
    }

    // The [GpuOperatorMacro] of G# on <operatorMethod>, with its two arguments, or null
    internal static AttributeData? GetMacroAttribute(IMethodSymbol? operatorMethod) =>
        operatorMethod?.GetAttribute(KnownAttributes.GpuOperatorMacro) is { } attribute &&
        attribute.AttributeClass?.ContainingNamespace.IsCoreNamespace() == true &&
        attribute.ConstructorArguments.Length >= 2
            ? attribute
            : null;

    // +, ++ (the operator as written)
    internal static string GetOperatorText(ExpressionSyntax operatorCall) => operatorCall switch
    {
        BinaryExpressionSyntax binary => binary.OperatorToken.Text,
        PrefixUnaryExpressionSyntax prefix => prefix.OperatorToken.Text,
        PostfixUnaryExpressionSyntax postfix => postfix.OperatorToken.Text,
        _ => string.Empty
    };

    // The operands of an operator call, as the target takes them after the result: a shift's count is the extra one
    internal static (ExpressionSyntax? Left, ExpressionSyntax? Right, ExpressionSyntax? Extra) ExtractOperands(
        ExpressionSyntax rightNode) => rightNode switch
        {
            BinaryExpressionSyntax { Left: var left, Right: var right } binary
                when binary.IsKind(SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression)
                => (left, null, right),
            BinaryExpressionSyntax { Left: var left, Right: var right }
                => (left, right, null),
            PrefixUnaryExpressionSyntax unary
                => (unary.Operand, null, null),
            _ => (null, null, null)
        };
}
