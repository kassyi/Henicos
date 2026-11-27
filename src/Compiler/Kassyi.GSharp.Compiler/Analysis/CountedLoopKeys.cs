using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

// A counted for loop whose own variable can serve as the iteration key: one int or uint variable, a unit step,
// a strict bound, an initializer that does not vary, and no other write to the variable
internal static class CountedLoopKeys
{
    internal static bool TryGetCountedLoopVariable(
        ForStatementSyntax forLoop,
        BlockSyntax methodBody,
        SemanticModel semanticModel,
        UniformityAnalysis? uniformity,
        [NotNullWhen(true)] out ILocalSymbol? loopVariable)
    {
        loopVariable = null;
        if (uniformity == null ||
            forLoop.SyntaxTree != semanticModel.SyntaxTree ||
            forLoop.Declaration is not { Variables: [{ Initializer.Value: { } initializerValue } declarator] } ||
            forLoop.Incrementors.Count != 1 ||
            forLoop.Condition is not { } condition)
        {
            return false;
        }

        var local = semanticModel.GetDeclaredSymbol<ILocalSymbol>(declarator);
        if (local is not { RefKind: RefKind.None } ||
            local.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_UInt32))
        {
            return false;
        }

        if (uniformity.IsThreadVariant(initializerValue))
        {
            return false;
        }

        if (!TryGetUnitStepDirection(forLoop.Incrementors[0], local, semanticModel, out var isIncrement))
        {
            return false;
        }

        if (!IsStrictBoundingComparison(condition, local, isIncrement, semanticModel))
        {
            return false;
        }

        if (WritesLocal(semanticModel.AnalyzeDataFlow(forLoop.Statement), local) ||
            WritesLocal(semanticModel.AnalyzeDataFlow(condition), local))
        {
            return false;
        }

        var hasAliasOrCapturedWrite = methodBody.DescendantNodes()
            .AsStackEnumerable()
            .Any((Model: semanticModel, Local: local), static (node, state) =>
                TakesReference(node, state.Model, state.Local) ||
                WritesCaptured(node, state.Model, state.Local));
        if (hasAliasOrCapturedWrite)
        {
            return false;
        }

        loopVariable = local;
        return true;
    }

    internal static bool TryGetUnitStepDirection(
        ExpressionSyntax incrementor,
        ILocalSymbol loopVariable,
        SemanticModel semanticModel,
        out bool isIncrement)
    {
        isIncrement = false;
        var operation = semanticModel.GetOperation(incrementor);
        switch (operation)
        {
            case IIncrementOrDecrementOperation { OperatorMethod: null, IsLifted: false } incOp
                when incOp.Target is ILocalReferenceOperation target &&
                     SymbolEqualityComparer.Default.Equals(target.Local, loopVariable):
                isIncrement = incOp.Kind == OperationKind.Increment;
                return incOp.Kind is OperationKind.Increment or OperationKind.Decrement;

            case ICompoundAssignmentOperation { OperatorMethod: null, IsLifted: false } compOp
                when compOp.Target is ILocalReferenceOperation target &&
                     SymbolEqualityComparer.Default.Equals(target.Local, loopVariable) &&
                     compOp.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract &&
                     compOp.Value.ConstantValue is { HasValue: true, Value: 1 or 1u }:
                isIncrement = compOp.OperatorKind == BinaryOperatorKind.Add;
                return true;

            default:
                return false;
        }
    }

    internal static bool IsStrictBoundingComparison(
        ExpressionSyntax condition,
        ILocalSymbol loopVariable,
        bool isIncrement,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(condition) is not IBinaryOperation
            {
                OperatorMethod: null,
                IsLifted: false
            } comparison)
        {
            return false;
        }

        BinaryOperatorKind kind;
        if (comparison.LeftOperand is ILocalReferenceOperation leftLocal &&
            SymbolEqualityComparer.Default.Equals(leftLocal.Local, loopVariable) &&
            SymbolEqualityComparer.Default.Equals(comparison.RightOperand.Type, loopVariable.Type))
        {
            kind = comparison.OperatorKind;
        }
        else if (comparison.RightOperand is ILocalReferenceOperation rightLocal &&
                 SymbolEqualityComparer.Default.Equals(rightLocal.Local, loopVariable) &&
                 SymbolEqualityComparer.Default.Equals(comparison.LeftOperand.Type, loopVariable.Type))
        {
            kind = FlipComparison(comparison.OperatorKind);
        }
        else
        {
            return false;
        }

        return (kind, isIncrement) switch
        {
            (BinaryOperatorKind.LessThan, true) => true,
            (BinaryOperatorKind.GreaterThan, false) => true,
            _ => false
        };
    }

    internal static BinaryOperatorKind FlipComparison(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
        BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
        _ => kind
    };

    internal static bool WritesLocal(DataFlowAnalysis? flow, ILocalSymbol local) =>
        flow is not { Succeeded: true } ||
        flow.WrittenInside.Contains(local) ||
        flow.UnsafeAddressTaken.Contains(local);

    internal static bool WritesCaptured(SyntaxNode node, SemanticModel model, ILocalSymbol local)
    {
        var body = node switch
        {
            AnonymousFunctionExpressionSyntax lambda => lambda.Body,
            LocalFunctionStatementSyntax { Body: { } block } => block,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } => expression,
            _ => null
        };
        return body != null && WritesLocal(model.AnalyzeDataFlow(body), local);
    }

    internal static bool TakesReference(SyntaxNode node, SemanticModel model, ILocalSymbol local)
    {
        var expression = node switch
        {
            ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword } arg =>
                arg.Expression,
            RefExpressionSyntax reference => reference.Expression,
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.AddressOfExpression } address => address.Operand,
            _ => null
        };
        return expression != null &&
               expression.UnwrapParentheses() is IdentifierNameSyntax identifier &&
               SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, local);
    }
}
