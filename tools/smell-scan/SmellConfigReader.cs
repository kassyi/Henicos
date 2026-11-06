using System.Collections.Immutable;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Reads and checks a smells file. The nodes are walked by hand (no reflection-based deserializer) so that every
/// problem is reported with the line of the node it belongs to.
/// </summary>
internal sealed class SmellConfigReader
{
    private static readonly string[] s_topKeys = ["version", "defaults", "rules"];
    private static readonly string[] s_defaultKeys = ["exclude"];
    private static readonly string[] s_ruleKeys =
        ["id", "detector", "severity", "parameters", "include", "exclude", "overrides", "suggestion", "help"];
    private static readonly string[] s_overrideKeys = ["files", "parameters"];

    private readonly string _file;

    private SmellConfigReader(string file)
    {
        _file = file;
    }

    /// <summary>Reads <paramref name="text"/>, which came from <paramref name="file"/>, and checks it.</summary>
    /// <exception cref="SmellConfigException">The text is not valid YAML or breaks a rule of the format.</exception>
    public static SmellConfig Read(string file, string text) => new SmellConfigReader(file).ReadText(text);

    private SmellConfig ReadText(string text)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException exception)
        {
            throw Error((int)exception.Start.Line, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            // YamlDotNet's scanner throws this for some unterminated flow collections and gives no position.
            throw Error(1, $"the YAML cannot be read ({exception.Message}); check for an unterminated '[' or '{{'");
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw Error(1, "the file must be a mapping with 'version' and 'rules'");
        }

        var top = Mapping(root, s_topKeys, "the file");
        RequireKey(top, "version", root);
        RequireKey(top, "rules", root);

        var version = Integer(top["version"]);
        if (version != 1)
        {
            throw Error(top["version"], $"unsupported version {version}; this tool reads version 1");
        }

        var defaultExclude = ImmutableArray<string>.Empty;
        if (top.TryGetValue("defaults", out var defaultsNode))
        {
            var defaults = Mapping(defaultsNode, s_defaultKeys, "'defaults'");
            if (defaults.TryGetValue("exclude", out var excludeNode))
            {
                defaultExclude = Strings(excludeNode, "'exclude'");
            }
        }

        var rules = ImmutableArray.CreateBuilder<SmellRule>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var ruleNode in Sequence(top["rules"], "'rules'"))
        {
            var rule = ReadRule(ruleNode);
            if (ids.TryGetValue(rule.Id, out var firstLine))
            {
                throw Error(ruleNode, $"rule id '{rule.Id}' is already used at line {firstLine}");
            }

            ids.Add(rule.Id, rule.Line);
            rules.Add(rule);
        }

        return new SmellConfig(defaultExclude, rules.ToImmutable());
    }

    private SmellRule ReadRule(YamlNode node)
    {
        var map = Mapping(node, s_ruleKeys, "a rule");
        RequireKey(map, "id", node);
        RequireKey(map, "detector", node);
        RequireKey(map, "severity", node);

        var id = Scalar(map["id"]);
        if (!IsKebabCase(id))
        {
            throw Error(map["id"], $"rule id '{id}' must be kebab-case (lowercase words joined by '-')");
        }

        var detectorName = Scalar(map["detector"]);
        var detector = DetectorRegistry.Find(detectorName)
            ?? throw Error(
                map["detector"],
                $"unknown detector '{detectorName}'; " + $"known detectors: {DetectorRegistry.NameList()}");

        var severityText = Scalar(map["severity"]);
        var severity = severityText switch
        {
            "off" => SmellSeverity.Off,
            "note" => SmellSeverity.Note,
            "warning" => SmellSeverity.Warning,
            "error" => SmellSeverity.Error,
            _ => throw Error(map["severity"], $"unknown severity '{severityText}'; use off, note, warning or error"),
        };

        var parameters = map.TryGetValue("parameters", out var parametersNode)
            ? Parameters(parametersNode, detector)
            : ImmutableDictionary<string, int>.Empty;

        var overrides = ImmutableArray.CreateBuilder<ParameterOverride>();
        if (map.TryGetValue("overrides", out var overridesNode))
        {
            foreach (var overrideNode in Sequence(overridesNode, "'overrides'"))
            {
                overrides.Add(ReadOverride(overrideNode, detector));
            }
        }

        return new SmellRule(
            id,
            detector,
            severity,
            parameters,
            map.TryGetValue("include", out var includeNode) ? Strings(includeNode, "'include'") : [],
            map.TryGetValue("exclude", out var excludeNode) ? Strings(excludeNode, "'exclude'") : [],
            overrides.ToImmutable(),
            map.TryGetValue("suggestion", out var suggestionNode) ? Scalar(suggestionNode) : string.Empty,
            map.TryGetValue("help", out var helpNode) ? Scalar(helpNode) : string.Empty,
            (int)node.Start.Line);
    }

    private ParameterOverride ReadOverride(YamlNode node, ISmellDetector detector)
    {
        var map = Mapping(node, s_overrideKeys, "an override");
        RequireKey(map, "files", node);
        RequireKey(map, "parameters", node);
        var files = Strings(map["files"], "'files'");
        if (files.IsEmpty)
        {
            throw Error(map["files"], "'files' must list at least one glob");
        }

        return new ParameterOverride(files, Parameters(map["parameters"], detector));
    }

    private ImmutableDictionary<string, int> Parameters(YamlNode node, ISmellDetector detector)
    {
        if (node is not YamlMappingNode map)
        {
            throw Error(node, "'parameters' must be a mapping");
        }

        var values = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var pair in map.Children)
        {
            var key = Scalar(pair.Key);
            var declared = FindParameter(detector, key)
                ?? throw Error(
                    pair.Key,
                    $"detector '{detector.Name}' has no parameter '{key}'; "
                    + $"it declares {ParameterNames(detector)}");
            var value = Integer(pair.Value);
            if (value < declared.Min)
            {
                throw Error(pair.Value, $"parameter '{key}' must be at least {declared.Min}, but is {value}");
            }

            if (!values.TryAdd(key, value))
            {
                throw Error(pair.Key, $"parameter '{key}' is set twice");
            }
        }

        return values.ToImmutable();
    }

    private static bool IsKebabCase(string id)
    {
        if (id.Length == 0 || id[0] is < 'a' or > 'z' || id[^1] == '-')
        {
            return false;
        }

        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            var isWord = c is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (!isWord && (c != '-' || id[i - 1] == '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static DetectorParameter? FindParameter(ISmellDetector detector, string name)
    {
        foreach (var parameter in detector.Parameters)
        {
            if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
            {
                return parameter;
            }
        }

        return null;
    }

    private static string ParameterNames(ISmellDetector detector)
    {
        var names = new List<string>(detector.Parameters.Length);
        foreach (var parameter in detector.Parameters)
        {
            names.Add(parameter.Name);
        }

        return string.Join(", ", names);
    }

    private Dictionary<string, YamlNode> Mapping(YamlNode node, string[] allowedKeys, string what)
    {
        if (node is not YamlMappingNode map)
        {
            throw Error(node, $"{what} must be a mapping");
        }

        var values = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var pair in map.Children)
        {
            var key = Scalar(pair.Key);
            if (Array.IndexOf(allowedKeys, key) < 0)
            {
                throw Error(pair.Key, $"unknown key '{key}' in {what}; allowed keys: {string.Join(", ", allowedKeys)}");
            }

            if (!values.TryAdd(key, pair.Value))
            {
                throw Error(pair.Key, $"key '{key}' appears twice in {what}");
            }
        }

        return values;
    }

    private void RequireKey(Dictionary<string, YamlNode> map, string key, YamlNode owner)
    {
        if (!map.ContainsKey(key))
        {
            throw Error(owner, $"missing required key '{key}'");
        }
    }

    private IEnumerable<YamlNode> Sequence(YamlNode node, string what)
    {
        if (node is not YamlSequenceNode sequence)
        {
            throw Error(node, $"{what} must be a list");
        }

        return sequence.Children;
    }

    private ImmutableArray<string> Strings(YamlNode node, string what)
    {
        var values = ImmutableArray.CreateBuilder<string>();
        foreach (var item in Sequence(node, what))
        {
            values.Add(Scalar(item));
        }

        return values.ToImmutable();
    }

    private string Scalar(YamlNode node)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw Error(node, "a single value is expected here");
        }

        return scalar.Value ?? string.Empty;
    }

    private int Integer(YamlNode node)
    {
        var text = Scalar(node);
        if (node is YamlScalarNode { Style: not ScalarStyle.Plain })
        {
            throw Error(node, $"'{text}' is quoted; write the integer without quotes");
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw Error(node, $"'{text}' is not an integer");
        }

        return value;
    }

    private SmellConfigException Error(YamlNode node, string problem) => Error((int)node.Start.Line, problem);

    private SmellConfigException Error(int line, string problem) => new(_file, line, problem);
}
