using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Decides which rules look at which file and with which parameters. The order is fixed: the default excludes first,
/// then the rule's excludes, then its includes; the overrides are applied in file order and the last match wins.
/// </summary>
internal sealed class RuleScope
{
    private readonly GlobSet _defaultExclude;
    private readonly RuleGlobs[] _rules;

    /// <summary>Compiles the globs of <paramref name="config"/>.</summary>
    public RuleScope(SmellConfig config)
    {
        _defaultExclude = new GlobSet(config.DefaultExclude);
        _rules = new RuleGlobs[config.Rules.Length];
        for (var i = 0; i < _rules.Length; i++)
        {
            var rule = config.Rules[i];
            var overrideFiles = new GlobSet[rule.Overrides.Length];
            for (var j = 0; j < overrideFiles.Length; j++)
            {
                overrideFiles[j] = new GlobSet(rule.Overrides[j].Files);
            }

            _rules[i] = new RuleGlobs(rule, new GlobSet(rule.Include), new GlobSet(rule.Exclude), overrideFiles);
        }
    }

    /// <summary>Returns true when no rule can look at <paramref name="path"/>, so the file need not be parsed.</summary>
    public bool IsOutOfScope(string path)
    {
        foreach (var rule in Rules(path))
        {
            return false;
        }

        return true;
    }

    /// <summary>Lists the rules that run on <paramref name="path"/>: not off, not excluded, and included.</summary>
    public IEnumerable<SmellRule> Rules(string path)
    {
        if (_defaultExclude.IsMatch(path))
        {
            yield break;
        }

        foreach (var globs in _rules)
        {
            if (globs.Rule.Severity == SmellSeverity.Off || globs.Exclude.IsMatch(path))
            {
                continue;
            }

            if (globs.Rule.Include.IsEmpty || globs.Include.IsMatch(path))
            {
                yield return globs.Rule;
            }
        }
    }

    /// <summary>Resolves the parameters of <paramref name="rule"/> for <paramref name="path"/>.</summary>
    public RuleParameters ParametersFor(SmellRule rule, string path)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var parameter in rule.Detector.Parameters)
        {
            values[parameter.Name] = parameter.Default;
        }

        foreach (var pair in rule.Parameters)
        {
            values[pair.Key] = pair.Value;
        }

        var globs = FindGlobs(rule);
        for (var i = 0; i < globs.Overrides.Length; i++)
        {
            if (!globs.Overrides[i].IsMatch(path))
            {
                continue;
            }

            foreach (var pair in rule.Overrides[i].Parameters)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return new RuleParameters(values);
    }

    private RuleGlobs FindGlobs(SmellRule rule)
    {
        foreach (var globs in _rules)
        {
            if (ReferenceEquals(globs.Rule, rule))
            {
                return globs;
            }
        }

        throw new ArgumentException($"Rule '{rule.Id}' does not belong to this scope.", nameof(rule));
    }

    private sealed record RuleGlobs(SmellRule Rule, GlobSet Include, GlobSet Exclude, GlobSet[] Overrides);
}
