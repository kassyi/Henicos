namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Incremental source generator that reads trusted_keys.txt from MSBuild AdditionalFiles and inlines public keys.
/// </summary>
[Generator]
public sealed class TrustedKeysGenerator : IIncrementalGenerator
{
    private const string TargetFileName = "trusted_keys.txt";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var trustedKeysFiles = context.AdditionalTextsProvider
            .Where(static file =>
            {
                var fileName = Path.GetFileName(file.Path);
                return string.Equals(fileName, TargetFileName, StringComparison.OrdinalIgnoreCase);
            })
            .Select(static (file, ct) => (file.Path, Text: file.GetText(ct)));

        var combinedFiles = trustedKeysFiles.Collect();

        context.RegisterSourceOutput(combinedFiles, static (spc, files) =>
        {
            if (files.IsEmpty)
            {
                return;
            }

            var builder = ImmutableArray.CreateBuilder<byte>();
            foreach (var file in files)
            {
                if (file.Text is not { } sourceText)
                {
                    continue;
                }

                ParseKeysAndReportDiagnostics(spc, file.Path, sourceText, builder);
            }

            if (builder.Count == 0)
            {
                return;
            }

            var keys = builder.ToImmutable();
            var source = GenerateSource(keys);
            spc.AddSource("GpuSecurityPolicy.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static void ParseKeysAndReportDiagnostics(
        SourceProductionContext spc,
        string path,
        SourceText sourceText,
        ImmutableArray<byte>.Builder builder)
    {
        foreach (var line in sourceText.Lines)
        {
            var lineSpan = line.Span;
            var lineText = sourceText.ToString(lineSpan);
            var trimmed = lineText.AsSpan().Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            if (ValidateAndParseLine(trimmed, out var keyBytes, out var reason))
            {
                builder.AddRange(keyBytes);
            }
            else
            {
                var lineNumber = line.LineNumber + 1;
                var location = Location.Create(path, lineSpan, sourceText.Lines.GetLinePositionSpan(lineSpan));
                spc.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.MalformedTrustedKey,
                    location,
                    path,
                    lineNumber,
                    reason));
            }
        }
    }

    private static bool ValidateAndParseLine(ReadOnlySpan<char> trimmed, out byte[] bytes, out string reason)
    {
        bytes = [];
        reason = string.Empty;

        if (trimmed.Length == 64)
        {
            if (TryParseHex(trimmed, out bytes))
            {
                return true;
            }

            reason = "invalid character";
            return false;
        }

        if (trimmed.Length == 44)
        {
            if (trimmed[43] == '=')
            {
                if (IsValidBase64Head(trimmed.Slice(0, 43)))
                {
                    bytes = Convert.FromBase64String(trimmed.ToString());
                    return true;
                }

                reason = "invalid character";
                return false;
            }

            reason = "expected 44 Base64 characters ending with '='";
            return false;
        }

        if (ContainsInvalidChar(trimmed))
        {
            reason = "invalid character";
            return false;
        }

        if (trimmed[trimmed.Length - 1] == '=' || HasChar(trimmed, '+') || HasChar(trimmed, '/'))
        {
            reason = "expected 44 Base64 characters ending with '='";
            return false;
        }

        reason = "expected 64 hex characters";
        return false;
    }

    private static bool IsValidBase64Head(ReadOnlySpan<char> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (!(c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsInvalidChar(ReadOnlySpan<char> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (!(c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/' or '='))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasChar(ReadOnlySpan<char> span, char target)
    {
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == target)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, out byte[] bytes)
    {
        bytes = new byte[32];
        for (var i = 0; i < 32; i++)
        {
            var hi = HexVal(hex[i * 2]);
            var lo = HexVal(hex[i * 2 + 1]);
            if (hi < 0 || lo < 0)
            {
                bytes = [];
                return false;
            }

            bytes[i] = (byte)((hi << 4) | lo);
        }

        return true;
    }

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static string GenerateSource(ImmutableArray<byte> keys)
    {
        using var writer = new SourceWriter();
        writer.AppendLine("// <auto-generated/>");
        writer.AppendLine("#nullable enable");
        writer.AppendLine("namespace Kassyi.GSharp.Runtime.Fatbin;");
        writer.AppendLine("public static partial class GpuSecurityPolicy");

        using (writer.Scope())
        {
            writer.AppendLine("private static readonly byte[] s_generatedTrustedKeys = new byte[]");
            using (writer.Scope("{", "};"))
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    writer.Append($"0x{keys[i]:x2}, ");

                    if (i % 8 == 7 || i == keys.Length - 1)
                    {
                        writer.AppendLine();
                    }
                }
            }

            writer.AppendLine();

            writer.AppendLine("static partial void GetCustomEmbeddedKeys(ref System.ReadOnlySpan<byte> keys)");
            using (writer.Scope())
            {
                writer.AppendLine("keys = s_generatedTrustedKeys;");
            }
        }

        return writer.ToString();
    }
}
