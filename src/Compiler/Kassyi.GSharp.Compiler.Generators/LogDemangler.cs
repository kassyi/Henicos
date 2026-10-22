using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// Replaces renamed G# identifiers (<c>__gsharp_k_*</c>, <c>__gsharp_fn_*</c>, <c>__gsharp_ty_*</c>, <c>__gsharp_id_*</c>,
/// <c>__gsharp_mb_*</c>, <c>__gsharp_tp_*</c> and hash-shortened <c>_h</c> names) in a compiler or runtime log with their C#
/// names.
/// </summary>
/// <remarks>
/// Decoding goes through <see cref="GSharpNameEncoding"/>, the same source the compiler encodes with, so the two cannot drift.
/// The <c>*.gsym.json</c> symbol maps come from an explicit path or from <c>obj/</c>, <c>src/</c> and <c>tests/</c> under the
/// current directory; a map whose companion <c>.g.cu</c> / <c>.g.cpp</c> carries a <c>// gsharp-symbols:</c> header must
/// match that hash.
/// </remarks>
public static class LogDemangler
{
    private const string SymbolFileSuffix = ".gsym.json";

    private static readonly string[] s_discoveryRoots = ["obj", "src", "tests"];

    private const string Usage =
        """
        Usage:
          dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle [<log|->] [<symbols>]
          <command> | dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle - [<symbols>]
          just gsharp-demangle <log|-> [symbols]

        Arguments:
          <log|->     Log file to demangle, or '-' for standard input (default).
          <symbols>   A *.gsym.json file, a companion *.g.cu / *.g.cpp file, or a directory to search.
                      Default: every *.gsym.json under ./obj, ./src and ./tests.

        Options:
          help, --help, -h   Show this help.
        """;

    /// <summary>Runs the <c>demangle</c> mode; <paramref name="args"/> excludes the mode name. Returns the exit code.</summary>
    public static int Run(string[] args)
    {
        var logPath = args.Length > 0 ? args[0] : "-";
        var symbolsPath = args.Length > 1 ? args[1] : "";
        if (IsHelp(logPath) || IsHelp(symbolsPath))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            var symbolMap = LoadSymbolMap(symbolsPath);
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            if (string.IsNullOrWhiteSpace(logPath) || logPath == "-")
            {
                if (!Console.IsInputRedirected)
                {
                    Console.WriteLine(Usage);
                    return 0;
                }

                // [WHY] Console.In decodes with the console code page, which garbles UTF-8 logs on Windows
                using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
                DecodeLines(input, output, symbolMap);
                return 0;
            }

            if (!File.Exists(logPath))
            {
                throw new FileNotFoundException($"Log file not found: {logPath}");
            }

            using (var input = new StreamReader(logPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                DecodeLines(input, output, symbolMap);
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static bool IsHelp(string arg) => arg is "help" or "--help" or "-h" or "-?" or "/?";

    private static void DecodeLines(TextReader input, TextWriter output, IReadOnlyDictionary<string, string> symbolMap)
    {
        while (input.ReadLine() is { } line)
        {
            output.WriteLine(GSharpNameEncoding.DecodeText(line, symbolMap));
        }
    }

    private static Dictionary<string, string> LoadSymbolMap(string explicitPath)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbolFile in FindSymbolFiles(explicitPath))
        {
            var content = File.ReadAllText(symbolFile, Encoding.UTF8);
            if (content.Length == 0)
            {
                continue;
            }

            foreach (var entry in GSharpNameEncoding.ParseSymbolFile(content, ReadCompanionHash(symbolFile)))
            {
                map[entry.Key] = entry.Value;
            }
        }

        return map;
    }

    private static List<string> FindSymbolFiles(string explicitPath)
    {
        var files = new List<string>();
        if (string.IsNullOrWhiteSpace(explicitPath))
        {
            foreach (var root in s_discoveryRoots)
            {
                if (Directory.Exists(root))
                {
                    files.AddRange(Directory.EnumerateFiles(root, "*" + SymbolFileSuffix, SearchOption.AllDirectories));
                }
            }

            return files;
        }

        if (Directory.Exists(explicitPath))
        {
            files.AddRange(Directory.EnumerateFiles(explicitPath, "*" + SymbolFileSuffix, SearchOption.AllDirectories));
        }
        else if (!File.Exists(explicitPath))
        {
            throw new FileNotFoundException($"Symbols path not found: {explicitPath}");
        }
        else if (explicitPath.EndsWith(SymbolFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            files.Add(explicitPath);
        }
        else if (File.Exists(explicitPath + SymbolFileSuffix))
        {
            files.Add(explicitPath + SymbolFileSuffix);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported symbols path or missing companion .gsym.json: {explicitPath}");
        }

        return files;
    }

    /// <summary>The <c>// gsharp-symbols:</c> hash on line 1 of the artifact next to <paramref name="symbolFile"/>, if any.</summary>
    private static string? ReadCompanionHash(string symbolFile)
    {
        var sourceFile = symbolFile[..^SymbolFileSuffix.Length];
        if (!File.Exists(sourceFile))
        {
            return null;
        }

        using var reader = File.OpenText(sourceFile);
        return reader.ReadLine() is { } firstLine && GSharpNameEncoding.TryReadSymbolHeaderHash(firstLine, out var hash)
            ? hash
            : null;
    }
}
