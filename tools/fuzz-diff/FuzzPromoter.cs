using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Represents a clean, self-contained differential regression case emitted by <see cref="FuzzPromoter"/>
/// ready to paste into <c>tests/Compiler/Kassyi.GSharp.Tests.Shared/Differential/</c>.
/// </summary>
public sealed record PromotedDifferentialArtifacts(
    string Fingerprint,
    string ReproKernelClassName,
    string ReproKernelMethodName,
    string ReproKernelFileContent,
    string ReproCaseSnippetContent,
    string KnownDivergenceEntry = "",
    string ReproFileStem = "");

/// <summary>
/// Emits clean, self-contained C# regression artifacts (<c>Repro_&lt;seed&gt;.cu.cs</c> and <c>DifferentialCase</c> snippet)
/// for reduced differential fuzzer findings.
/// </summary>
public static class FuzzPromoter
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Formats a reduced kernel source, fuzz seed, input seed, failing paths, and fingerprint into a clean
    /// <c>Differential/</c> kernel file, companion <c>DifferentialCase</c> snippet, and <c>KnownDivergence</c> entry.
    /// </summary>
    public static PromotedDifferentialArtifacts BuildPromotionArtifacts(
        string reducedKernelSource,
        ulong fuzzSeed,
        int inputSeed,
        string failingPaths = "Native,Cuda",
        string failureKind = "CellMismatch",
        string? explicitFingerprint = null,
        string? targetKernelName = null,
        string column = "hash")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reducedKernelSource);

        var fpResult = FuzzDeduplicator.ComputeFingerprint(reducedKernelSource, failingPaths, failureKind);
        string fingerprint = string.IsNullOrWhiteSpace(explicitFingerprint) ? fpResult.Fingerprint : explicitFingerprint;
        string normalizedColumn = string.IsNullOrWhiteSpace(column) ? "hash" : column.AsSpan().Trim().ToString();

        var root = FuzzSyntaxHelpers.ParseRoot(reducedKernelSource);
        var kernelMethods = FuzzSyntaxHelpers.GetKernelMethods(root);
        var selectedMethod = SelectKernelMethod(root, kernelMethods, targetKernelName)
            ?? throw new InvalidOperationException("No kernel method found in reducedKernelSource.");

        var (reproClassName, reproMethodName, reproFileStem, seedHex) =
            ResolveReproNames(selectedMethod, fuzzSeed, targetKernelName, kernelMethods.Count);

        var renamedMethod = selectedMethod.WithIdentifier(SyntaxFactory.Identifier(reproMethodName));

        string headerLine = BuildHeaderLine(seedHex, inputSeed, fingerprint, fpResult);

        string knownDivergenceEntry = BuildKnownDivergenceEntry(
            reproClassName,
            reproMethodName,
            normalizedColumn,
            fpResult.FailingPathsMask);

        string kernelFileContent = BuildReproKernelFile(root, renamedMethod, reproClassName, headerLine);
        string caseSnippetContent = BuildCaseSnippet(
            reproClassName,
            reproMethodName,
            inputSeed,
            headerLine,
            normalizedColumn,
            knownDivergenceEntry);

        return new PromotedDifferentialArtifacts(
            fingerprint,
            reproClassName,
            reproMethodName,
            kernelFileContent,
            caseSnippetContent,
            knownDivergenceEntry,
            reproFileStem);
    }

    private static string BuildHeaderLine(
        string seedHex,
        int inputSeed,
        string fingerprint,
        FuzzFingerprintResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"// Found by the differential fuzzer: fuzz-seed {seedHex}, input-seed {inputSeed}, ") +
        string.Create(
            CultureInfo.InvariantCulture,
            $"fingerprint {fingerprint} (paths: {result.FailingPathsMask}, kind: {result.FailureKindTag})");

    private static string BuildCaseSnippet(
        string reproClassName,
        string reproMethodName,
        int inputSeed,
        string headerLine,
        string column,
        string knownDivergenceEntry)
    {
        string caseProperty = FuzzCaseEmitter.RenderDifferentialCaseProperty(
            reproClassName,
            reproMethodName,
            inputSeed,
            headerLine,
            column);
        return
            $$"""
            {{caseProperty}}

            // KnownDivergence entry:
            {{knownDivergenceEntry}}
            """;
    }

    private static (string ClassName, string MethodName, string FileStem, string SeedHex) ResolveReproNames(
        MethodDeclarationSyntax selectedMethod,
        ulong fuzzSeed,
        string? targetKernelName,
        int totalKernelCount)
    {
        bool hasKernelSeed = TryExtractKernelSeed(selectedMethod, out ulong extractedKernelSeed);
        ulong effectiveSeed = hasKernelSeed ? extractedKernelSeed : fuzzSeed;
        string seedHex = FuzzRandom.FormatSeed(effectiveSeed);
        string shortSeedHex = string.Create(CultureInfo.InvariantCulture, $"{effectiveSeed:X8}");

        bool needsMethodDisambiguator = !hasKernelSeed &&
            (!string.IsNullOrWhiteSpace(targetKernelName) || totalKernelCount > 1);
        string methodSuffix = needsMethodDisambiguator
            ? $"_{SanitizeIdentifier(selectedMethod.Identifier.ValueText)}"
            : string.Empty;

        string reproClassName = $"FuzzRepro_{shortSeedHex}{methodSuffix}_Kernels";
        string reproMethodName = $"Repro_{shortSeedHex}{methodSuffix}";
        string reproFileStem = $"Repro_{seedHex}{methodSuffix}";
        return (reproClassName, reproMethodName, reproFileStem, seedHex);
    }

    /// <summary>
    /// Writes the promoted <c>Repro_&lt;seed&gt;.cu.cs</c> and <c>Repro_&lt;seed&gt;.Case.cs</c> into <c>&lt;findingsDir&gt;/&lt;sanitizedFingerprint&gt;/</c>.
    /// </summary>
    public static string WritePromotedFinding(
        string findingsDir,
        PromotedDifferentialArtifacts artifacts,
        ulong fuzzSeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(findingsDir);
        ArgumentNullException.ThrowIfNull(artifacts);

        string safeBucketName = SanitizeDirectoryName(artifacts.Fingerprint);
        string bucketDir = Path.Combine(Path.GetFullPath(findingsDir), safeBucketName);
        Directory.CreateDirectory(bucketDir);

        string fileStem = !string.IsNullOrWhiteSpace(artifacts.ReproFileStem)
            ? artifacts.ReproFileStem
            : $"Repro_{FuzzRandom.FormatSeed(fuzzSeed)}";
        string kernelPath = Path.Combine(bucketDir, $"{fileStem}.cu.cs");
        string casePath = Path.Combine(bucketDir, $"{fileStem}.Case.cs");

        File.WriteAllText(kernelPath, artifacts.ReproKernelFileContent, s_utf8NoBom);
        File.WriteAllText(casePath, artifacts.ReproCaseSnippetContent, s_utf8NoBom);
        return kernelPath;
    }

    /// <summary>Converts a fingerprint like <c>fnv1a64:0123456789abcdef</c> into a filesystem-safe directory name across Windows and Unix.</summary>
    public static string SanitizeDirectoryName(string fingerprint) =>
        string.Create(fingerprint.Length, fingerprint, static (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                span[i] = c is ':' or '/' or '\\' ? '_' : c;
            }
        });

    private static MethodDeclarationSyntax? SelectKernelMethod(
        CompilationUnitSyntax root,
        IReadOnlyList<MethodDeclarationSyntax> kernelMethods,
        string? targetKernelName)
    {
        if (!string.IsNullOrWhiteSpace(targetKernelName))
        {
            foreach (var m in kernelMethods)
            {
                if (string.Equals(m.Identifier.ValueText, targetKernelName, StringComparison.Ordinal))
                {
                    return m;
                }
            }
        }

        if (kernelMethods.Count > 0)
        {
            return kernelMethods[0];
        }

        foreach (var node in root.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax method)
            {
                return method;
            }
        }

        return null;
    }

    private static string SanitizeIdentifier(string raw) =>
        string.Create(raw.Length, raw, static (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                span[i] = char.IsLetterOrDigit(c) || c == '_' ? c : '_';
            }
        });

    private static bool TryExtractKernelSeed(MethodDeclarationSyntax method, out ulong kernelSeed)
    {
        kernelSeed = 0UL;
        foreach (var trivia in method.GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                continue;
            }

            string text = trivia.ToString();
            const string Marker = "fuzz-seed:";
            int idx = text.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            ReadOnlySpan<char> rest = text.AsSpan(idx + Marker.Length).TrimStart();
            int end = 0;
            while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] == '_'))
            {
                end++;
            }

            if (end > 0 && FuzzRandom.TryParseSeed(rest[..end].ToString(), out kernelSeed))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildKnownDivergenceEntry(
        string reproClassName,
        string reproMethodName,
        string column,
        string canonicalPathsMask)
    {
        var parts = FuzzSyntaxHelpers.SplitNonEmpty(canonicalPathsMask, ",");
        var flags = new List<string>(parts.Count);
        foreach (string rawPart in parts)
        {
            ReadOnlySpan<char> part = rawPart.AsSpan().Trim();
            if (part.Equals("Managed", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("DiffPath.Managed");
            }
            else if (part.Equals("Native", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("DiffPath.Native");
            }
            else if (part.Equals("Cuda", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("DiffPath.Cuda");
            }
        }

        string diffPathFlags = flags.Count > 0 ? string.Join(" | ", flags) : "DiffPath.Native | DiffPath.Cuda";
        return $"new(\"{reproClassName}.{reproMethodName}\", \"{column}\", {diffPathFlags}, Issue: 0),";
    }

    private static string BuildReproKernelFile(
        CompilationUnitSyntax root,
        MethodDeclarationSyntax renamedMethod,
        string reproClassName,
        string headerLine)
    {
        using var writer = new SourceWriter();
        FuzzCaseEmitter.AppendKernelsFileHeader(writer, "Kassyi.GSharp.Tests.Shared.Differential", headerLine);

        var rewriter = new BatchClassQualifierRewriter(reproClassName);
        var rewrittenMethod = (MethodDeclarationSyntax)rewriter.Visit(renamedMethod);
        string methodText = FuzzSyntaxHelpers.RenderFullNode(rewrittenMethod).AsSpan().TrimEnd().ToString();

        var rewrittenHelpers = CollectUsedRewrittenHelpers(root, rewriter, methodText);
        AppendDeclarationsAndClass(writer, reproClassName, methodText, rewrittenHelpers);

        return writer.ToString();
    }

    private static List<string> CollectUsedRewrittenHelpers(
        CompilationUnitSyntax root,
        BatchClassQualifierRewriter rewriter,
        string methodText)
    {
        var helperDecls = FuzzSyntaxHelpers.GetDeviceFunctionMethods(root);
        bool anyHelperUsed = false;
        foreach (var h in helperDecls)
        {
            if (methodText.Contains(h.Identifier.ValueText, StringComparison.Ordinal))
            {
                anyHelperUsed = true;
                break;
            }
        }

        var rewrittenHelpers = new List<string>(helperDecls.Count);
        if (anyHelperUsed)
        {
            foreach (var helper in helperDecls)
            {
                var rewrittenHelper = (MethodDeclarationSyntax)rewriter.Visit(helper);
                rewrittenHelpers.Add(FuzzSyntaxHelpers.RenderFullNode(rewrittenHelper).AsSpan().Trim().ToString());
            }
        }

        return rewrittenHelpers;
    }

    private static void AppendDeclarationsAndClass(
        SourceWriter writer,
        string reproClassName,
        string methodText,
        List<string> rewrittenHelpers)
    {
        bool usesEnum = methodText.Contains("FuzzEnum", StringComparison.Ordinal) ||
                        ContainsInAny(rewrittenHelpers, static h => h.Contains("FuzzEnum", StringComparison.Ordinal));
        if (usesEnum)
        {
            ScalarFuzzGenerator.AppendEnumDeclarations(writer);
            writer.AppendLine();
        }

        bool usesStruct = ContainsCompositeTypeReference(methodText) ||
                          ContainsInAny(rewrittenHelpers, ContainsCompositeTypeReference);
        if (usesStruct)
        {
            FuzzCompositeModel.AppendCompositeStructDeclarations(writer);
            writer.AppendLine();
        }

        using (writer.Scope($"public static partial class {reproClassName}"))
        {
            foreach (string helperText in rewrittenHelpers)
            {
                AppendNormalizedLines(writer, helperText);
                writer.AppendLine();
            }

            AppendNormalizedLines(writer, methodText);
        }
    }

    private static bool ContainsInAny(IReadOnlyList<string> items, Func<string, bool> predicate)
    {
        foreach (string item in items)
        {
            if (predicate(item))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCompositeTypeReference(string text) =>
        text.Contains("FuzzStruct", StringComparison.Ordinal) ||
        text.Contains("FuzzWrapInt", StringComparison.Ordinal) ||
        text.Contains("FuzzPackedStruct", StringComparison.Ordinal) ||
        text.Contains("FuzzNullableBag", StringComparison.Ordinal);

    private static void AppendNormalizedLines(SourceWriter writer, string text)
    {
        foreach (string line in FuzzSyntaxHelpers.SplitNonEmpty(text, "\r\n"))
        {
            ReadOnlySpan<char> trimmed = line.AsSpan().TrimStart();
            if (!trimmed.IsEmpty)
            {
                writer.AppendLine(trimmed.ToString());
            }
        }
    }

    private sealed class BatchClassQualifierRewriter(string targetClassName) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            if (visited.Expression is IdentifierNameSyntax { Identifier.ValueText: "FuzzBatchKernels" } id)
            {
                return visited.WithExpression(SyntaxFactory.IdentifierName(targetClassName).WithTriviaFrom(id));
            }

            return visited;
        }
    }
}
