namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>A loop in a method body that requires iteration keying on CUDA.</summary>
internal sealed record KeyedLoop(
    StatementSyntax LoopOrHeader,
    IterationKeyKind Kind,
    int CounterIndex,
    ILocalSymbol? LoopVariable,
    StatementSyntax? ResetStatement,
    LabeledStatementSyntax? IncrementLabel);
