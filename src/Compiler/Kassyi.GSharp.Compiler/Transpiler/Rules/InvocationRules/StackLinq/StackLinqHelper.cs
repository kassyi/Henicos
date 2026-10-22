namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal static class StackLinqHelper
{
    public static bool IsStackLinqMethod(IMethodSymbol symbol) =>
        symbol.ContainingNamespace is
        {
            Name: "Linq",
            ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.Name: "Kassyi" }
        };

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

    public static void EmitAccumulateElement(
        ref TranspileContext context,
        in LinqStage stage,
        string resultVar,
        int valIndex)
    {
        if (HasPredicateOrSelector(stage.Invocation, stage.Symbol))
        {
            var arg = GetLambdaArgument(stage.Invocation, stage.Symbol);
            context.Emitter.Writer.Append($"{resultVar} += (");
            context.Visitor.Visit(arg.Expression);
            context.Emitter.Writer.AppendLine($")(__gsharp_val_{valIndex});");
        }
        else
        {
            context.Emitter.Writer.AppendLine($"{resultVar} += __gsharp_val_{valIndex};");
        }
    }
}
