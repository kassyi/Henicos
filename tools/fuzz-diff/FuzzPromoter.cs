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
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

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

        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(reducedKernelSource, s_parseOptions).GetRoot();
        var kernelMethods = FuzzReducer.GetKernelMethods(root);
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

        var kernelFileSb = new StringBuilder();
        kernelFileSb.AppendLine(headerLine);
        kernelFileSb.AppendLine("#nullable enable");
        kernelFileSb.AppendLine("#pragma warning disable CS0162, CS0219, CS1717, CS1718");
        kernelFileSb.AppendLine("using System;");
        kernelFileSb.AppendLine("using Kassyi.GSharp.Core;");
        kernelFileSb.AppendLine();
        kernelFileSb.AppendLine("namespace Kassyi.GSharp.Tests.Shared.Differential;");
        kernelFileSb.AppendLine();

        // Include any referenced custom enums so the file is self-contained.
        string methodText = renamedMethod.ToFullString().TrimEnd();
        if (methodText.Contains("FuzzEnum", StringComparison.Ordinal))
        {
            ScalarFuzzGenerator.AppendEnumDeclarations(kernelFileSb);
            kernelFileSb.AppendLine();
        }

        kernelFileSb.AppendLine($"public static partial class {reproClassName}");
        kernelFileSb.AppendLine("{");

        foreach (var helper in FuzzReducer.GetDeviceFunctionMethods(root))
        {
            if (methodText.Contains(helper.Identifier.ValueText, StringComparison.Ordinal))
            {
                kernelFileSb.AppendLine(IndentMethod(helper.ToFullString().Trim()));
                kernelFileSb.AppendLine();
            }
        }

        kernelFileSb.AppendLine(IndentMethod(methodText.Trim()));
        kernelFileSb.AppendLine("}");

        var caseSb = new StringBuilder();
        caseSb.AppendLine(headerLine);
        caseSb.AppendLine($"public static DifferentialCase {reproMethodName}_Case {{ get; }} = new(\"{reproClassName}.{reproMethodName}\")");
        caseSb.AppendLine("{");
        caseSb.AppendLine("    Shape = LaunchShapes.PartialWarp(),");
        caseSb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    Seeds = [{inputSeed}],"));
        caseSb.AppendLine("    Inputs = static (rng, shape) =>");
        caseSb.AppendLine("    {");
        caseSb.AppendLine("        int rows = shape.Rows;");
        caseSb.AppendLine("        var (inA, inB) = DiffValues.Int32Pairs(rng, rows);");
        caseSb.AppendLine("        var inC = DiffValues.Values(rng, rows, DiffValues.ShiftCounts, DiffValues.NextInt32);");
        caseSb.AppendLine("        return new DiffBuffers()");
        caseSb.AppendLine("            .In(\"inA\", inA)");
        caseSb.AppendLine("            .In(\"inB\", inB)");
        caseSb.AppendLine("            .In(\"inC\", inC)");
        caseSb.AppendLine("            .Out<ulong>(\"checksum\", rows, [\"hash\"], OutputPolicy.Exact);");
        caseSb.AppendLine("    },");
        caseSb.AppendLine($"    Launch = static (b, s) => {reproClassName}.Launch{reproMethodName}(");
        caseSb.AppendLine("        b.Get<int>(\"inA\"),");
        caseSb.AppendLine("        b.Get<int>(\"inB\"),");
        caseSb.AppendLine("        b.Get<int>(\"inC\"),");
        caseSb.AppendLine("        b.Get<ulong>(\"checksum\"),");
        caseSb.AppendLine("        gridDimX: s.GridX,");
        caseSb.AppendLine("        blockDimX: s.BlockX),");
        caseSb.AppendLine($"    Oracle = static (b, _) => {reproClassName}.{reproMethodName}(");
        caseSb.AppendLine("        b.Get<int>(\"inA\"),");
        caseSb.AppendLine("        b.Get<int>(\"inB\"),");
        caseSb.AppendLine("        b.Get<int>(\"inC\"),");
        caseSb.AppendLine("        b.Get<ulong>(\"checksum\")),");
        caseSb.AppendLine("};");

        return new PromotedDifferentialArtifacts(
            fingerprint,
            reproClassName,
            reproMethodName,
            kernelFileSb.ToString(),
            caseSb.ToString());
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

    private static string IndentMethod(string methodText)
    {
        var lines = methodText.Split(["\r\n", "\n"], StringSplitOptions.None);
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("    ", StringComparison.Ordinal))
            {
                sb.Append(line);
            }
            else
            {
                sb.Append("    ").Append(line);
            }
        }

        return sb.ToString();
    }
}
