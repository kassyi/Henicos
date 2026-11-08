namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The variables (two, for a ref conditional) that a location belongs to, as symbols or names, and whether it touches
/// memory: what <see cref="EffectWalker.AddLocation"/> returns.
/// </summary>
internal readonly struct EffectRoot
{
    public ImmutableArray<object> Variables { get; }

    public bool IsMemory { get; }

    public EffectRoot(object? variable, bool isMemory = false)
    {
        Variables = variable != null ? ImmutableArray.Create(variable) : ImmutableArray<object>.Empty;
        IsMemory = isMemory || variable == null;
    }

    public EffectRoot(ImmutableArray<object> variables, bool isMemory)
    {
        Variables = variables;
        IsMemory = isMemory || variables.IsEmpty;
    }

    public static EffectRoot Combine(EffectRoot whenTrue, EffectRoot whenFalse)
    {
        var isMemory = whenTrue.IsMemory || whenFalse.IsMemory;
        var builder = ImmutableArray.CreateBuilder<object>(whenTrue.Variables.Length + whenFalse.Variables.Length);
        builder.AddRange(whenTrue.Variables);
        foreach (var v in whenFalse.Variables)
        {
            if (!builder.Contains(v))
            {
                builder.Add(v);
            }
        }

        return new EffectRoot(builder.ToImmutable(), isMemory);
    }
}
