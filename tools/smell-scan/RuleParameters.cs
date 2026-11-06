namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The parameter values of one rule for one file: detector defaults, then the rule, then each matching override.</summary>
internal sealed class RuleParameters
{
    private readonly Dictionary<string, int> _values;

    /// <summary>Creates the values from a copy of <paramref name="values"/>.</summary>
    public RuleParameters(IReadOnlyDictionary<string, int> values)
    {
        _values = new Dictionary<string, int>(values.Count, StringComparer.Ordinal);
        foreach (var pair in values)
        {
            _values[pair.Key] = pair.Value;
        }
    }

    /// <summary>Gets the value of <paramref name="name"/>.</summary>
    /// <exception cref="KeyNotFoundException">The detector did not declare the parameter.</exception>
    public int Get(string name) => _values[name];

    /// <summary>Returns a copy with <paramref name="name"/> set to <paramref name="value"/>.</summary>
    public RuleParameters With(string name, int value)
    {
        var copy = new RuleParameters(_values);
        copy._values[name] = value;
        return copy;
    }
}
