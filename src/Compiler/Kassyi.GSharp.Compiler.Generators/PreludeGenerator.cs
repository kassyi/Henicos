using System.Globalization;
using Kassyi.GSharp.Hashing;

namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// Resolves and embeds <c>Prelude.cu</c> and <c>Prelude.cpp</c> (including relative <c>#include</c> directives)
/// into compile-time C# string constants, eliminating runtime <c>System.Reflection</c> resource lookups in the compiler.
/// </summary>
public static class PreludeGenerator
{
    private const string IncludePrefix = "#include \"";

    /// <summary>
    /// Reads prelude resource files from <paramref name="resourcesDir"/> and writes <c>GSharpPreludeResources.g.cs</c> to
    /// <paramref name="outputDir"/>, with the hash of the preludes and of <c>InternalNames.yml</c> in the same directory.
    /// </summary>
    public static void Run(string resourcesDir, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var cudaPrelude = LoadAndResolvePrelude(resourcesDir, "Prelude.cu", isCuda: true);
        var cppPrelude = LoadAndResolvePrelude(resourcesDir, "Prelude.cpp", isCuda: false);

        var internalNames = File.ReadAllText(Path.Combine(resourcesDir, "InternalNames.yml"), Encoding.UTF8);
        var source = GenerateSource(cudaPrelude, cppPrelude, internalNames);
        File.WriteAllText(Path.Combine(outputDir, "GSharpPreludeResources.g.cs"), source, Encoding.UTF8);
    }

    private static string LoadAndResolvePrelude(string resourcesDir, string rootFileName, bool isCuda)
    {
        var rootPath = Path.Combine(resourcesDir, rootFileName);
        using var reader = new StreamReader(rootPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd() + "\n";
        return RemoveIndentation(ResolveEmbeddedIncludes(resourcesDir, content, isCuda));
    }

    // Generated code is left-aligned (transpiler-emission §3); the resources stay indented for reading. They contain no
    // raw string literal, so the leading whitespace of a line is never part of a value.
    private static string RemoveIndentation(string text)
    {
        var builder = new StringBuilder(text.Length);
        var atLineStart = true;
        foreach (var c in text)
        {
            if (atLineStart && c is ' ' or '\t')
            {
                continue;
            }

            builder.Append(c);
            atLineStart = c == '\n';
        }

        return builder.ToString();
    }

    private static string ResolveEmbeddedIncludes(string resourcesDir, string content, bool isCuda)
    {
        var searchStart = 0;

        while (true)
        {
            var includeIndex = content.IndexOf(IncludePrefix, searchStart, StringComparison.Ordinal);
            if (includeIndex < 0)
            {
                break;
            }

            var pathStart = includeIndex + IncludePrefix.Length;
            var pathEnd = content.IndexOf('"', pathStart);
            if (pathEnd < 0)
            {
                break;
            }

            var relativeInclude = string.Create(pathEnd - pathStart, (content, pathStart), static (span, state) =>
            {
                var src = state.content.AsSpan(state.pathStart, span.Length);
                for (var i = 0; i < span.Length; i++)
                {
                    var c = src[i];
                    span[i] = c is '/' or '\\' ? Path.DirectorySeparatorChar : c;
                }
            });
            var includedFilePath = Path.Combine(resourcesDir, relativeInclude);
            if (!File.Exists(includedFilePath))
            {
                searchStart = pathEnd + 1;
                continue;
            }

            string includedContent;
            using (var includeReader =
                   new StreamReader(includedFilePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                includedContent = includeReader.ReadToEnd();
            }

            var before = content[..includeIndex];
            var after = content[(pathEnd + 1)..];
            if (!isCuda && after.AsSpan().Trim().IsEmpty)
            {
                includedContent = TrimOneTrailingNewline(includedContent);
            }

            content = before + includedContent + after;
            searchStart = includeIndex;
        }

        return content;
    }

    private static string TrimOneTrailingNewline(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        if (text.EndsWith("\n", StringComparison.Ordinal))
        {
            return text[..^1];
        }

        return text;
    }

    private static string GenerateSource(string cudaPrelude, string cppPrelude, string internalNames)
    {
        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        writer.Line();
        writer.Line("namespace Kassyi.GSharp.Compiler;");
        writer.Line();
        writer.AppendExcludeFromCodeCoverageAttribute();
        writer.Line("internal static class GSharpPreludeResources");
        using (writer.Scope())
        {
            writer.Append("public const string Cuda = \"");
            AppendEscapedStringLiteral(writer, cudaPrelude);
            writer.AppendLine("\";");
            writer.Line();
            writer.Append("public const string Cpp = \"");
            AppendEscapedStringLiteral(writer, cppPrelude);
            writer.AppendLine("\";");
            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// The FNV-1a 64 hash of <see cref=\"Cuda\"/>, <see cref=\"Cpp\"/> and <c>InternalNames.yml</c> (the spelling of the");
            writer.Line("/// generated names), without carriage returns.");
            writer.Line("/// </summary>");
            writer.AppendLine($"public const string Hash = \"{ComputeHash(cudaPrelude, cppPrelude, internalNames)}\";");
        }

        return writer.ToString();
    }

    // [WHY] A device code export records this hash with its record layout (GpuDeviceExportAttribute.PreludeHash), and a
    // consumer compares it with its own: an imported text calls the prelude's helpers and spells the generated names as
    // InternalNames.yml does. Carriage returns are skipped, so a Windows checkout (CRLF) and a WSL one (LF) give the same
    // hash.
    private static string ComputeHash(string cudaPrelude, string cppPrelude, string internalNames)
    {
        var hash = new Fnv1A64();
        foreach (var c in cudaPrelude + cppPrelude + internalNames)
        {
            if (c != '\r')
            {
                hash.Append(c);
            }
        }

        return hash.Value.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static void AppendEscapedStringLiteral(SourceWriter writer, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    writer.Append(@"\\");
                    break;
                case '"':
                    writer.Append("\\\"");
                    break;
                case '\r':
                    writer.Append(@"\r");
                    break;
                case '\n':
                    writer.Append(@"\n");
                    break;
                case '\t':
                    writer.Append(@"\t");
                    break;
                default:
                    writer.Append(c);
                    break;
            }
        }
    }
}
