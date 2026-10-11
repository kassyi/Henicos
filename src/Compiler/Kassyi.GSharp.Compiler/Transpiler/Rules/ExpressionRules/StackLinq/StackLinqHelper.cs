namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules.StackLinq;

internal static class StackLinqHelper
{
    public static ArgumentSyntax GetLambdaArgument(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        if (symbol.IsExtensionMethod && invocation.Expression is MemberAccessExpressionSyntax)
        {
            return invocation.ArgumentList.Arguments[0];
        }

        return invocation.ArgumentList.Arguments[1];
    }

    public static bool HasPredicateOrSelector(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        if (symbol.IsExtensionMethod && invocation.Expression is MemberAccessExpressionSyntax)
        {
            return invocation.ArgumentList.Arguments.Count > 0;
        }

        return invocation.ArgumentList.Arguments.Count > 1;
    }
}
