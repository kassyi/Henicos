namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// A set of variables that an expression reads or writes: locals and by-value parameters by symbol (or by name, for an
/// identifier that lowering synthesized and the semantic model does not know), and two coarse members:
/// <see cref="Memory"/> (array elements, pointers, shared memory, <c>ref</c> parameters, static fields) and
/// <see cref="AllLocals"/> (a call that may touch any local: a local function or a delegate).
/// </summary>
internal struct VariableSet
{
    private ImmutableArray<object>.Builder? _variables;

    /// <summary>Memory that is not a local: two sets with <see cref="Memory"/> overlap.</summary>
    public bool Memory;

    /// <summary>Every local: overlaps any set that names a local.</summary>
    public bool AllLocals;

    public readonly bool IsEmpty => !Memory && !AllLocals && _variables is not { Count: > 0 };

    private readonly bool HasVariables => AllLocals || _variables is { Count: > 0 };

    /// <summary>
    /// Adds a local or by-value parameter (<see cref="ISymbol"/>) or the name of an unresolved identifier.
    /// </summary>
    public void Add(object variable)
    {
        _variables ??= ImmutableArray.CreateBuilder<object>();
        if (!Contains(variable))
        {
            _variables.Add(variable);
        }
    }

    /// <summary>The variables of this set and of <paramref name="other"/>, in a set of its own.</summary>
    public readonly VariableSet Union(in VariableSet other)
    {
        var union = new VariableSet { Memory = Memory || other.Memory, AllLocals = AllLocals || other.AllLocals };
        union.AddAll(_variables);
        union.AddAll(other._variables);
        return union;
    }

    private void AddAll(ImmutableArray<object>.Builder? variables)
    {
        if (variables == null)
        {
            return;
        }

        foreach (var variable in variables)
        {
            Add(variable);
        }
    }

    /// <summary>Whether a variable of this set may be the same as a variable of <paramref name="other"/>.</summary>
    public readonly bool Overlaps(in VariableSet other)
    {
        if ((Memory && other.Memory) || (AllLocals && other.HasVariables) || (other.AllLocals && HasVariables))
        {
            return true;
        }

        return _variables != null &&
               _variables
                   .AsStackEnumerable()
                   .Any(other, static (variable, set) => set.Contains(variable));
    }

    /// <summary>Whether every variable of this set is a variable of <paramref name="other"/>.</summary>
    public readonly bool IsSubsetOf(in VariableSet other)
    {
        if ((Memory && !other.Memory) || (AllLocals && !other.AllLocals))
        {
            return false;
        }

        return other.AllLocals ||
               _variables == null ||
               _variables
                   .AsStackEnumerable()
                   .All(other, static (variable, set) => set.Contains(variable));
    }

    /// <summary>The members of the set, for test messages: <c>{memory, all-locals, x, y}</c>.</summary>
    public override readonly string ToString()
    {
        var writer = new SourceWriter();
        writer.Append("{");
        var separator = "";
        if (Memory)
        {
            writer.Append("memory");
            separator = ", ";
        }

        if (AllLocals)
        {
            writer.Append($"{separator}all-locals");
            separator = ", ";
        }

        foreach (var variable in _variables ?? (IEnumerable<object>)[])
        {
            writer.Append($"{separator}{(variable as ISymbol)?.Name ?? variable.ToString()}");
            separator = ", ";
        }

        writer.Append("}");
        return writer.ToString();
    }

    /// <summary>Whether the set names <paramref name="variable"/> (a symbol or a name).</summary>
    public readonly bool Contains(object variable) =>
        _variables != null &&
        _variables
            .AsStackEnumerable()
            .Any(variable, static (existing, v) => existing is ISymbol symbol
                ? v is ISymbol other && symbol.IsEqualTo(other)
                : existing.Equals(v));
}
