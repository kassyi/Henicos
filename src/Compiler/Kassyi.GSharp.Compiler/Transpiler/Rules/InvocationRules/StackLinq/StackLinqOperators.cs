namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

/// <summary>Reads and classifies <see cref="StackLinqOperator"/> values.</summary>
internal static class StackLinqOperators
{
    /// <summary>The operator named <paramref name="name"/> (a method name) when a fused chain can contain it.</summary>
    public static bool TryParse(string name, out StackLinqOperator op)
    {
        StackLinqOperator? parsed = name switch
        {
            nameof(StackLinqOperator.Where) => StackLinqOperator.Where,
            nameof(StackLinqOperator.Select) => StackLinqOperator.Select,
            nameof(StackLinqOperator.Take) => StackLinqOperator.Take,
            nameof(StackLinqOperator.Skip) => StackLinqOperator.Skip,
            nameof(StackLinqOperator.TakeWhile) => StackLinqOperator.TakeWhile,
            nameof(StackLinqOperator.SkipWhile) => StackLinqOperator.SkipWhile,
            nameof(StackLinqOperator.TakeLast) => StackLinqOperator.TakeLast,
            nameof(StackLinqOperator.SkipLast) => StackLinqOperator.SkipLast,
            nameof(StackLinqOperator.Sum) => StackLinqOperator.Sum,
            nameof(StackLinqOperator.Count) => StackLinqOperator.Count,
            nameof(StackLinqOperator.Any) => StackLinqOperator.Any,
            nameof(StackLinqOperator.All) => StackLinqOperator.All,
            nameof(StackLinqOperator.First) => StackLinqOperator.First,
            nameof(StackLinqOperator.FirstOrDefault) => StackLinqOperator.FirstOrDefault,
            nameof(StackLinqOperator.Last) => StackLinqOperator.Last,
            nameof(StackLinqOperator.LastOrDefault) => StackLinqOperator.LastOrDefault,
            nameof(StackLinqOperator.Max) => StackLinqOperator.Max,
            nameof(StackLinqOperator.Min) => StackLinqOperator.Min,
            nameof(StackLinqOperator.Average) => StackLinqOperator.Average,
            nameof(StackLinqOperator.Aggregate) => StackLinqOperator.Aggregate,
            nameof(StackLinqOperator.Contains) => StackLinqOperator.Contains,
            _ => null
        };
        op = parsed.GetValueOrDefault();
        return parsed.HasValue;
    }

    /// <summary>Whether <paramref name="op"/> ends a fused chain.</summary>
    public static bool IsTerminal(this StackLinqOperator op) => op >= StackLinqOperator.Sum;

    /// <summary>Whether <paramref name="op"/> keeps or drops elements by their position.</summary>
    public static bool IsPositionStage(this StackLinqOperator op) =>
        op is StackLinqOperator.Skip or StackLinqOperator.Take or
            StackLinqOperator.SkipLast or StackLinqOperator.TakeLast;
}
