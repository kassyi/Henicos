using System.Numerics;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Proves that the step of a for loop counter cannot overflow, so that <see cref="IntegerSemantics"/> keeps it as the
/// C++ operator (#722).
/// </summary>
internal static class BoundedLoopStep
{
    /// <summary>
    /// Whether <paramref name="node"/> is the step <c>i++</c> / <c>++i</c> (<c>i--</c> / <c>--i</c>) of a for loop whose
    /// condition is <c>i &lt; e</c> (<c>i &gt; e</c>) compared in the type of <c>i</c>, or <c>i &lt;= c</c>
    /// (<c>i &gt;= c</c>) with a constant short of the end of the type, and where nothing else writes <c>i</c>: the
    /// condition held for the value that the step adds 1 to, so that value is below the maximum and the step cannot
    /// overflow. Kept as the C++ operator, it keeps nvcc's induction variable widening (naive SGEMM: 202 SASS
    /// instructions with it, 70 without; 11.97 ms against 12.95 ms).
    /// </summary>
    /// <remarks>
    /// "Nothing else writes i": not the body, the condition or another step (Roslyn data flow), no lambda or local
    /// function of the method writes it, and no ref, out or address of it is taken anywhere in the method (a ref local
    /// could write it in the body). A lowered tree (a CPU state machine) is not analyzed, and its steps wrap.
    /// </remarks>
    public static bool IsBounded(IIncrementOrDecrementOperation operation, ExpressionSyntax node, SemanticModel model)
    {
        if (node.Parent is not ForStatementSyntax { Condition: { } condition } loop || model.SyntaxTree != node.SyntaxTree ||
            GetCounter(operation.Target) is not { } counter ||
            model.GetOperation(condition) is not IBinaryOperation { OperatorMethod: null, IsLifted: false } comparison ||
            !IsBoundingComparison(comparison, counter, operation.Kind == OperationKind.Increment))
        {
            return false;
        }

        if (WritesCounter(model.AnalyzeDataFlow(loop.Statement), counter) ||
            WritesCounter(model.AnalyzeDataFlow(condition), counter))
        {
            return false;
        }

        if (loop.Incrementors
            .AsStackEnumerable()
            .Any(
                (node, model, counter),
                static (incrementor, state) => incrementor != state.node &&
                                               WritesCounter(state.model.AnalyzeDataFlow(incrementor), state.counter)
            ))
        {
            return false;
        }

        return GetMethodBody(loop) is { } body &&
               !body.DescendantNodes()
                   .AsStackEnumerable()
                   .Any((model, counter), static (n, state) => TakesReference(n, state.model, state.counter) ||
                                                               WritesCaptured(n, state.model, state.counter));
    }

    private static ISymbol? GetCounter(IOperation target) => target switch
    {
        ILocalReferenceOperation { Local: { RefKind: RefKind.None } local } => local,
        IParameterReferenceOperation { Parameter: { RefKind: RefKind.None } parameter } => parameter,
        _ => null
    };

    // i < e (or e > i) for a step up, i > e (or e < i) for a step down, compared in the type of i; <= and >= only
    // against a constant short of the end of the type
    private static bool IsBoundingComparison(IBinaryOperation comparison, ISymbol counter, bool increment)
    {
        BinaryOperatorKind kind;
        IOperation bound;
        if (IsCounter(comparison.LeftOperand, counter))
        {
            (kind, bound) = (comparison.OperatorKind, comparison.RightOperand);
        }
        else if (IsCounter(comparison.RightOperand, counter))
        {
            (kind, bound) = (Flip(comparison.OperatorKind), comparison.LeftOperand);
        }
        else
        {
            return false;
        }

        var type = comparison.LeftOperand.Type;
        if (type is null || IntegerSemantics.GetWidth(type) is not (var width and not 0) ||
            !IntegerSemantics.IsSigned(type) ||
            !SymbolEqualityComparer.Default.Equals(type, comparison.RightOperand.Type))
        {
            return false;
        }

        var constant = bound.ConstantValue is { HasValue: true, Value: { } value } &&
                       IntegerSemantics.TryGetInteger(value, out var c)
            ? c
            : (BigInteger?)null;
        return (kind, increment) switch
        {
            (BinaryOperatorKind.LessThan, true) => true,
            (BinaryOperatorKind.LessThanOrEqual, true) => constant < IntegerSemantics.MaxValue(width),
            (BinaryOperatorKind.GreaterThan, false) => true,
            (BinaryOperatorKind.GreaterThanOrEqual, false) => constant > IntegerSemantics.MinValue(width),
            _ => false
        };
    }

    private static bool IsCounter(IOperation operand, ISymbol counter) =>
        GetCounter(operand) is { } symbol && SymbolEqualityComparer.Default.Equals(symbol, counter);

    private static BinaryOperatorKind Flip(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
        BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual,
        BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
        BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
        _ => kind
    };

    private static bool WritesCounter(DataFlowAnalysis? flow, ISymbol counter) =>
        flow is not { Succeeded: true } || flow.WrittenInside.Contains(counter) ||
        flow.UnsafeAddressTaken.Contains(counter);

    // The body of the method that holds the loop, through the lambdas and local functions around it: a delegate made
    // anywhere in the method may write a counter it captures
    private static SyntaxNode? GetMethodBody(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is BaseMethodDeclarationSyntax method)
            {
                return (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression;
            }
        }

        return null;
    }

    // A lambda or local function that writes i (it may run in the loop body); one that only reads it does not matter
    private static bool WritesCaptured(SyntaxNode node, SemanticModel model, ISymbol counter)
    {
        var body = node switch
        {
            AnonymousFunctionExpressionSyntax lambda => lambda.Body,
            LocalFunctionStatementSyntax { Body: { } block } => block,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } => expression,
            _ => null
        };
        return body != null && WritesCounter(model.AnalyzeDataFlow(body), counter);
    }

    // ref i, out i, ref i as a ref local initializer, &i
    private static bool TakesReference(SyntaxNode node, SemanticModel model, ISymbol counter)
    {
        var expression = node switch
        {
            ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword } argument =>
                argument.Expression,
            RefExpressionSyntax reference => reference.Expression,
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.AddressOfExpression } address => address.Operand,
            _ => null
        };
        // ref (i) and &(i) name the counter as well
        return expression != null &&
               IntegerSemanticsEmitter.Unparenthesize(expression) is IdentifierNameSyntax identifier &&
               SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, counter);
    }
}
