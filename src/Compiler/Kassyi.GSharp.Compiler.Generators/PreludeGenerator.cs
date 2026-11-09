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

    private static readonly StringComparer s_pathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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
        var rootPath = Path.GetFullPath(Path.Combine(resourcesDir, rootFileName));
        if (!File.Exists(rootPath))
        {
            throw new GeneratorInputException(rootPath, 0, "The prelude file does not exist.");
        }

        var content = ReadText(rootPath) + "\n";
        var includeChain = new List<string> { rootPath };
        return RemoveIndentation(ResolveEmbeddedIncludes(
            resourcesDir, rootPath, content, isCuda, atDocumentEnd: true, includeChain));
    }

    // Generated code is left-aligned (transpiler-emission §3); the resources stay indented for reading. They contain no
    // raw string literal, so the leading whitespace of a line is never part of a value.
    private static string RemoveIndentation(string text)
    {
        using var unindented = new SourceWriter();
        var atLineStart = true;
        foreach (var c in text)
        {
            if (atLineStart && c is ' ' or '\t')
            {
                continue;
            }

            unindented.Append(c);
            atLineStart = c == '\n';
        }

        return unindented.ToString();
    }

    // Replaces each #include "<path>" of content (the text of file) with the resolved text of <resourcesDir>/<path>.
    // atDocumentEnd: nothing but whitespace follows this text in the whole prelude. For the C++ prelude, a header
    // included there loses one trailing line break. includeChain: the files being resolved, outermost first.
    private static string ResolveEmbeddedIncludes(
        string resourcesDir,
        string file,
        string content,
        bool isCuda,
        bool atDocumentEnd,
        List<string> includeChain)
    {
        using var resolved = new SourceWriter();
        var copied = 0;

        while (true)
        {
            var includeIndex = content.IndexOf(IncludePrefix, copied, StringComparison.Ordinal);
            if (includeIndex < 0)
            {
                break;
            }

            var line = LineOf(content, includeIndex);
            var pathStart = includeIndex + IncludePrefix.Length;
            var pathEnd = content.IndexOfAny(['"', '\n'], pathStart);
            if (pathEnd < 0 || content[pathEnd] != '"')
            {
                throw new GeneratorInputException(
                    file, line, "The #include directive has no closing quote on its line.");
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
            var includedFilePath = Path.GetFullPath(Path.Combine(resourcesDir, relativeInclude));
            if (!File.Exists(includedFilePath))
            {
                throw new GeneratorInputException(file, line,
                    $"Cannot find the included file '{content[pathStart..pathEnd]}' ({includedFilePath}); " +
                    "prelude includes are relative to the resources directory.");
            }

            if (includeChain.FindIndex(p => s_pathComparer.Equals(p, includedFilePath)) >= 0)
            {
                throw new GeneratorInputException(
                    file, line, $"Include cycle: {DescribeCycle(includeChain, includedFilePath)}.");
            }

            var includedAtDocumentEnd = atDocumentEnd && content.AsSpan(pathEnd + 1).Trim().IsEmpty;
            var includedContent = ReadText(includedFilePath);
            if (!isCuda && includedAtDocumentEnd)
            {
                includedContent = TrimOneTrailingNewline(includedContent);
            }

            includeChain.Add(includedFilePath);
            var resolvedInclude = ResolveEmbeddedIncludes(
                resourcesDir,
                includedFilePath,
                includedContent,
                isCuda,
                includedAtDocumentEnd,
                includeChain
            );
            includeChain.RemoveAt(includeChain.Count - 1);

            resolved.Append(content, copied, includeIndex - copied);
            resolved.Append(resolvedInclude);
            copied = pathEnd + 1;
        }

        resolved.Append(content, copied, content.Length - copied);
        return resolved.ToString();
    }

    // "Prelude.cu -> a.cuh -> b.cuh -> a.cuh" (file names only).
    private static string DescribeCycle(List<string> includeChain, string repeated)
    {
        using var cycle = new SourceWriter();
        foreach (var path in includeChain)
        {
            cycle.Append($"{Path.GetFileName(path)} -> ");
        }

        cycle.Append(Path.GetFileName(repeated));
        return cycle.ToString();
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            line += text[i] == '\n' ? 1 : 0;
        }

        return line;
    }

    private static string ReadText(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
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
            writer.AppendLine($"public const string Cuda = {CSharpStringLiteral.Quote(cudaPrelude)};");
            writer.Line();
            writer.AppendLine($"public const string Cpp = {CSharpStringLiteral.Quote(cppPrelude)};");
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
}
