namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal readonly record struct StackLinqChain(
    ExpressionSyntax SourceArrayExpression,
    ImmutableArray<LinqStage> Stages,
    StackLinqOperator Terminal,
    bool IsParallel);
