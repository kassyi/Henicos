namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

/// <summary>
/// The StackLinq operators that a fused device chain can contain: the stages, then the terminals (keep every stage
/// before <see cref="Sum"/>). <see cref="StackLinqOperators.TryParse"/> is the one place that reads them from a method
/// name.
/// </summary>
internal enum StackLinqOperator
{
    Where,
    Select,
    Take,
    Skip,
    TakeWhile,
    SkipWhile,
    TakeLast,
    SkipLast,
    Sum,
    Count,
    Any,
    All,
    First,
    FirstOrDefault,
    Last,
    LastOrDefault,
    Max,
    Min,
    Average,
    Aggregate,
    Contains
}
