namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal readonly record struct LinqStage(
    StackLinqOperator Operator,
    InvocationExpressionSyntax Invocation,
    IMethodSymbol Symbol);
