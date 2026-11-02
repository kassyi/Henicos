using System.Globalization;
using System.Linq;
using System.Text;
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
    string ReproCaseSnippetContent);

/// <summary>
/// Emits clean, self-contained C# regression artifacts (<c>Repro_&lt;seed&gt;.cu.cs</c> and <c>DifferentialCase</c> snippet)
/// for reduced differential fuzzer findings.
/// </summary>
public static class FuzzPromoter
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Formats a reduced kernel source, fuzz seed, input seed, failing paths, and fingerprint into a clean
    /// <c>Differential/</c> kernel file and companion <c>DifferentialCase</c> snippet.
    /// </summary>
    public static PromotedDifferentialArtifacts BuildPromotionArtifacts(
        string reducedKernelSource,
        ulong fuzzSeed,
        int inputSeed,
        string failingPaths = "Native,Cuda",
        string failureKind = "CellMismatch",
        string? explicitFingerprint = null,
        string? targetKernelName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reducedKernelSource);

        var fpResult = FuzzDeduplicator.ComputeFingerprint(reducedKernelSource, failingPaths, failureKind);
        string fingerprint = string.IsNullOrWhiteSpace(explicitFingerprint) ? fpResult.Fingerprint : explicitFingerprint;
        string seedHex = FuzzRandom.FormatSeed(fuzzSeed);
        string shortSeedHex = string.Create(CultureInfo.InvariantCulture, $"{fuzzSeed:X8}");

        var root = FuzzSyntaxHelpers.ParseRoot(reducedKernelSource);
        var kernelMethods = FuzzSyntaxHelpers.GetKernelMethods(root);
        var selectedMethod = (!string.IsNullOrWhiteSpace(targetKernelName)
                ? kernelMethods.FirstOrDefault(m => string.Equals(m.Identifier.ValueText, targetKernelName, StringComparison.Ordinal))
                : null)
            ?? kernelMethods.FirstOrDefault()
            ?? root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("No kernel method found in reducedKernelSource.");

        string reproClassName = $"FuzzRepro_{shortSeedHex}_Kernels";
        string reproMethodName = $"Repro_{shortSeedHex}";
        var renamedMethod = selectedMethod.WithIdentifier(SyntaxFactory.Identifier(reproMethodName));

        string headerLine = string.Create(
            CultureInfo.InvariantCulture,
            $"// Found by the differential fuzzer: fuzz-seed {seedHex}, input-seed {inputSeed}, fingerprint {fingerprint} (paths: {fpResult.FailingPathsMask}, kind: {fpResult.FailureKindTag})");

        string kernelFileContent = BuildReproKernelFile(root, renamedMethod, reproClassName, headerLine);
        string caseSnippetContent = BuildReproCaseSnippet(reproClassName, reproMethodName, inputSeed, headerLine);

        return new PromotedDifferentialArtifacts(
            fingerprint,
            reproClassName,
            reproMethodName,
            kernelFileContent,
            caseSnippetContent);
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

        string seedHex = FuzzRandom.FormatSeed(fuzzSeed);
        string kernelPath = Path.Combine(bucketDir, $"Repro_{seedHex}.cu.cs");
        string casePath = Path.Combine(bucketDir, $"Repro_{seedHex}.Case.cs");

        File.WriteAllText(kernelPath, artifacts.ReproKernelFileContent, s_utf8NoBom);
        File.WriteAllText(casePath, artifacts.ReproCaseSnippetContent, s_utf8NoBom);
        return kernelPath;
    }

    /// <summary>Converts a fingerprint like <c>fnv1a64:0123456789abcdef</c> into a filesystem-safe directory name across Windows and Unix.</summary>
    public static string SanitizeDirectoryName(string fingerprint) =>
        fingerprint.Replace(':', '_').Replace('/', '_').Replace('\\', '_');

    private static string BuildReproKernelFile(
        CompilationUnitSyntax root,
        MethodDeclarationSyntax renamedMethod,
        string reproClassName,
        string headerLine)
    {
        using var writer = new SourceWriter();
        writer.AppendLine(headerLine);
        writer.AppendLine("#nullable enable");
        writer.AppendLine("#pragma warning disable CS0162, CS0219, CS1717, CS1718");
        writer.AppendLine("using System;");
        writer.AppendLine("using Kassyi.GSharp.Core;");
        writer.AppendLine();
        writer.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
        writer.AppendLine();

        string methodText = renamedMethod.ToFullString().TrimEnd();
        var helperDecls = FuzzSyntaxHelpers.GetDeviceFunctionMethods(root);
        var allHelperText = new StringBuilder();
        foreach (var helper in helperDecls)
        {
            allHelperText.AppendLine(helper.ToFullString());
        }

        string combinedText = methodText + "\n" + allHelperText;
        if (combinedText.Contains("FuzzEnum", StringComparison.Ordinal))
        {
            ScalarFuzzGenerator.AppendEnumDeclarations(writer);
            writer.AppendLine();
        }

        if (combinedText.Contains("FuzzStruct", StringComparison.Ordinal) ||
            combinedText.Contains("FuzzWrapInt", StringComparison.Ordinal) ||
            combinedText.Contains("FuzzPackedStruct", StringComparison.Ordinal) ||
            combinedText.Contains("FuzzNullableBag", StringComparison.Ordinal))
        {
            FuzzCompositeModel.AppendCompositeStructDeclarations(writer);
            writer.AppendLine();
        }

        using (writer.Scope($"public static partial class {reproClassName}"))
        {
            bool anyHelperUsed = helperDecls.Any(h => methodText.Contains(h.Identifier.ValueText, StringComparison.Ordinal));
            if (anyHelperUsed)
            {
                foreach (var helper in helperDecls)
                {
                    string hText = LeftAlignLines(
                        helper.ToFullString().Trim().Replace("FuzzBatchKernels.", $"{reproClassName}.", StringComparison.Ordinal));
                    writer.AppendLine(hText);
                    writer.AppendLine();
                }
            }

            string finalMethodText = LeftAlignLines(
                methodText.Trim().Replace("FuzzBatchKernels.", $"{reproClassName}.", StringComparison.Ordinal));
            writer.AppendLine(finalMethodText);
        }

        return writer.ToString();
    }

    private static string BuildReproCaseSnippet(
        string reproClassName,
        string reproMethodName,
        int inputSeed,
        string headerLine)
    {
        using var writer = new SourceWriter();
        writer.AppendLine(headerLine);
        using (writer.Scope($"public static DifferentialCase {reproMethodName}_Case {{ get; }} = new(\"{reproClassName}.{reproMethodName}\")", "};"))
        {
            writer.AppendLine("Shape = LaunchShapes.PartialWarp(),");
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Seeds = [{inputSeed}],"));
            using (writer.Scope("Inputs = static (rng, shape) =>", "},"))
            {
                writer.AppendLine("int rows = shape.Rows;");
                writer.AppendLine("var (inA, inB) = DiffValues.Int32Pairs(rng, rows);");
                writer.AppendLine("var inC = DiffValues.Values(rng, rows, DiffValues.ShiftCounts, DiffValues.NextInt32);");
                writer.AppendLine("return new DiffBuffers()");
                writer.AppendLine(".In(\"inA\", inA)");
                writer.AppendLine(".In(\"inB\", inB)");
                writer.AppendLine(".In(\"inC\", inC)");
                writer.AppendLine(".Out<ulong>(\"checksum\", rows, [\"hash\"], OutputPolicy.Exact);");
            }

            writer.AppendLine($"Launch = static (b, s) => {reproClassName}.Launch{reproMethodName}(");
            writer.AppendLine("b.Get<int>(\"inA\"),");
            writer.AppendLine("b.Get<int>(\"inB\"),");
            writer.AppendLine("b.Get<int>(\"inC\"),");
            writer.AppendLine("b.Get<ulong>(\"checksum\"),");
            writer.AppendLine("gridDimX: s.GridX,");
            writer.AppendLine("blockDimX: s.BlockX),");
            writer.AppendLine($"Oracle = static (b, _) => {reproClassName}.{reproMethodName}(");
            writer.AppendLine("b.Get<int>(\"inA\"),");
            writer.AppendLine("b.Get<int>(\"inB\"),");
            writer.AppendLine("b.Get<int>(\"inC\"),");
            writer.AppendLine("b.Get<ulong>(\"checksum\")),");
        }

        return writer.ToString();
    }

    private static string LeftAlignLines(string text)
    {
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        using var writer = new SourceWriter();
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (!string.IsNullOrEmpty(trimmed))
            {
                writer.AppendLine(trimmed);
            }
        }

        return writer.ToString().TrimEnd();
    }
}
