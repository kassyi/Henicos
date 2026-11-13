namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// XML documentation output shared by <see cref="NativeIntrinsicEmitter"/> and <c>NativeBindingEmitter</c>.
/// </summary>
internal static class NativeApiDocumentation
{
    public static void EmitDocumentation(SourceWriter writer, ApiMethodDefinition m)
    {
        if (m.XmlDoc is { } xmlDoc)
        {
            EmitXmlDoc(writer, xmlDoc);
            return;
        }

        if (!string.IsNullOrEmpty(m.Summary))
        {
            writer.Line($"/// <summary>{EscapeXml(m.Summary ?? string.Empty)}</summary>");
        }

        foreach (var t in m.TypeParameters)
        {
            if (!string.IsNullOrEmpty(t.Description))
            {
                writer.Line($"/// <typeparam name=\"{t.Name}\">{EscapeXml(t.Description ?? string.Empty)}</typeparam>");
            }
        }

        foreach (var p in m.Parameters)
        {
            if (!string.IsNullOrEmpty(p.Description))
            {
                writer.Line($"/// <param name=\"{p.Name}\">{EscapeXml(p.Description ?? string.Empty)}</param>");
            }
        }

        if (!string.IsNullOrEmpty(m.ReturnDescription))
        {
            writer.Line($"/// <returns>{EscapeXml(m.ReturnDescription ?? string.Empty)}</returns>");
        }
    }

    /// <summary>
    /// Writes each line of <paramref name="xmlDoc"/> unchanged after <c>///</c> (<c>xmlDoc</c> keys hold XML markup).
    /// A line ends at every line break that C# source knows (<see cref="XmlDocEscaping.LineBreaks"/>; <c>\r\n</c> is
    /// one), so no text of the YAML can end the comment and be read as code.
    /// </summary>
    public static void EmitXmlDoc(SourceWriter writer, string xmlDoc)
    {
        var start = 0;
        while (start <= xmlDoc.Length)
        {
            var end = xmlDoc.IndexOfAny(XmlDocEscaping.LineBreaks, start);
            if (end < 0)
            {
                end = xmlDoc.Length;
            }

            writer.Line($"/// {xmlDoc[start..end]}");
            start = end + 1;
            if (end + 1 < xmlDoc.Length && xmlDoc[end] == '\r' && xmlDoc[end + 1] == '\n')
            {
                start++;
            }
        }
    }

    /// <summary>
    /// Escapes XML special characters and keeps multi-line text inside the <c>///</c> comment.
    /// </summary>
    private static string EscapeXml(string text) => XmlDocEscaping.Escape(text, lineBreak: "\n/// ");
}
