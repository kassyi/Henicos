using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Reconverging;

/// <summary>
/// The lowered conditions of the <see langword="while"/> loops of a body, kept keyed by the loops as they are
/// rebuilt.
/// </summary>
internal sealed class IrLoopConditionTable(ImmutableDictionary<IrWhile, IrLoweredCondition> conditions)
{
    private readonly ImmutableDictionary<IrWhile, IrLoweredCondition>.Builder _conditions = conditions.ToBuilder();

    /// <summary>
    /// Records that <paramref name="previous"/> has been replaced by <paramref name="rebuilt"/>: the lowered condition
    /// of <paramref name="previous"/> (if it has one) now belongs to <paramref name="rebuilt"/>.
    /// </summary>
    public void Rebuilt(IrWhile previous, IrWhile rebuilt)
    {
        if (ReferenceEquals(previous, rebuilt))
        {
            return;
        }

        if (_conditions.TryGetValue(previous, out var condition))
        {
            _conditions.Remove(previous);
            _conditions.Add(rebuilt, condition);
        }
    }

    /// <summary>Returns the lowered loop conditions keyed by the current loop instances.</summary>
    public ImmutableDictionary<IrWhile, IrLoweredCondition> ToImmutable() => _conditions.ToImmutable();
}
