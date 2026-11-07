using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Writes the JSON files of <c>fuzz-diff run</c> (<c>finding.json</c>, <c>occurrences.jsonl</c>,
/// <c>summary.json</c>) in one layout: UTF-8 without a byte order mark, <c>\n</c> line breaks, non-ASCII text and
/// HTML characters as they are, and a final line break.
/// </summary>
internal static class FuzzJson
{
    /// <summary>UTF-8 without a byte order mark, for every file the run writes.</summary>
    internal static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions s_indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions s_line = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes <paramref name="node"/> indented, with a final line break.</summary>
    internal static void Write(string path, JsonNode node) =>
        File.WriteAllText(path, node.ToJsonString(s_indented) + "\n", Utf8NoBom);

    /// <summary>Writes <paramref name="value"/> indented, with a final line break.</summary>
    internal static void WriteRecord<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, s_indented) + "\n", Utf8NoBom);

    /// <summary>Appends <paramref name="node"/> as one line to the JSON Lines file <paramref name="path"/>.</summary>
    internal static void AppendLine(string path, JsonNode node) =>
        File.AppendAllText(path, node.ToJsonString(s_line) + "\n", Utf8NoBom);
}
