namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal readonly record struct LinqStage(string Name, InvocationExpressionSyntax Invocation, IMethodSymbol Symbol);

internal readonly record struct StackLinqChain(
    ExpressionSyntax SourceArrayExpression,
    ImmutableArray<LinqStage> Stages,
    string TerminalMethodName,
    bool IsParallel);
