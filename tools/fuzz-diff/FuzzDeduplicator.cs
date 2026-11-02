using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Result of computing a canonical AST fingerprint for a reduced differential fuzz kernel.
/// </summary>
public sealed record FuzzFingerprintResult(
    string Fingerprint,
    ulong Hash64,
    string NormalizedAstSkeleton,
    string FailingPathsMask,
    string FailureKindTag);

/// <summary>
/// Computes a normalized AST fingerprint for reduced kernels so duplicate
/// failures with different variable names, method names, custom type names, literal constants, or unreferenced dead declarations
/// collapse to the same bucket:
/// <para>
/// <c>Fingerprint = FNV1a64(NormalizedAstSkeleton + "|" + FailingPathsMask + "|" + FailureKindTag)</c>
/// </para>
/// </summary>
public static class FuzzDeduplicator
{
    /// <summary>The 64-bit FNV-1a offset basis.</summary>
    public const ulong FnvOffsetBasis = 14695981039346656037UL;

    /// <summary>The 64-bit FNV-1a prime multiplier.</summary>
    public const ulong FnvPrime = 1099511628211UL;

    /// <summary>
    /// Computes the canonical AST fingerprint and normalized skeleton for <paramref name="kernelSource"/>.
    /// </summary>
    public static FuzzFingerprintResult ComputeFingerprint(
        string kernelSource,
        string failingPaths = "Native,Cuda",
        string failureKind = "CellMismatch")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kernelSource);

        string canonicalPaths = NormalizeFailingPathsMask(failingPaths);
        string canonicalFailureKind = string.IsNullOrWhiteSpace(failureKind) ? "CellMismatch" : failureKind.Trim();
        string skeleton = BuildNormalizedAstSkeleton(kernelSource);

        string payload = $"{skeleton}|{canonicalPaths}|{canonicalFailureKind}";
        ulong hash64 = ComputeFnv1a64(payload);
        string fingerprint = string.Create(CultureInfo.InvariantCulture, $"fnv1a64:{hash64:x16}");

        return new FuzzFingerprintResult(fingerprint, hash64, skeleton, canonicalPaths, canonicalFailureKind);
    }

    /// <summary>
    /// Computes the 64-bit FNV-1a hash of a UTF-8 string.
    /// </summary>
    public static ulong ComputeFnv1a64(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ulong hash = FnvOffsetBasis;
        int byteCount = Encoding.UTF8.GetByteCount(text);
        byte[] bytes = new byte[byteCount];
        Encoding.UTF8.GetBytes(text, bytes);
        for (int i = 0; i < bytes.Length; i++)
        {
            hash = unchecked((hash ^ bytes[i]) * FnvPrime);
        }

        return hash;
    }

    /// <summary>
    /// Normalizes a failing paths list (e.g. <c>"Cuda, Native"</c>) into a deterministic ordered mask (<c>"Native,Cuda"</c>).
    /// </summary>
    public static string NormalizeFailingPathsMask(string? failingPaths)
    {
        if (string.IsNullOrWhiteSpace(failingPaths))
        {
            return "Unknown";
        }

        var parts = failingPaths
            .Split([',', '|', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static p => GetPathOrder(p))
            .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .Select(static p => NormalizePathName(p));

        string joined = string.Join(",", parts);
        return string.IsNullOrEmpty(joined) ? "Unknown" : joined;
    }

    /// <summary>
    /// Parses <paramref name="kernelSource"/> with Roslyn, strips dead/unreferenced local declarations and redundant parentheses,
    /// alpha-normalizes all identifiers (<c>v0, v1, ...</c> for parameters/locals, <c>M0, M1, ...</c> for methods, <c>T0, T1, ...</c> for custom types),
    /// and normalizes literals to canonical type tokens (<c>LIT_INT</c>, <c>LIT_UINT</c>, <c>LIT_LONG</c>, <c>LIT_ULONG</c>, <c>LIT_BOOL</c>, <c>LIT_CHAR</c>).
    /// </summary>
    public static string BuildNormalizedAstSkeleton(string kernelSource)
    {
        var root = FuzzSyntaxHelpers.ParseRoot(kernelSource);
        var typeMap = BuildCustomTypeNormalizationMap(root);
        var activeMethods = CollectActiveMethods(root);
        var methodMap = BuildMethodNormalizationMap(activeMethods);

        var sb = new StringBuilder();
        for (int i = 0; i < activeMethods.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            AppendNormalizedMethod(sb, activeMethods[i], methodMap, typeMap);
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> BuildCustomTypeNormalizationMap(CompilationUnitSyntax root)
    {
        var typeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (typeDecl is StructDeclarationSyntax or EnumDeclarationSyntax)
            {
                string origName = typeDecl.Identifier.ValueText;
                if (!typeMap.ContainsKey(origName))
                {
                    typeMap[origName] = $"T{typeMap.Count}";
                }
            }
        }

        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            string name = id.Identifier.ValueText;
            if (name.StartsWith("FuzzEnum", StringComparison.Ordinal) && !typeMap.ContainsKey(name))
            {
                typeMap[name] = $"T{typeMap.Count}";
            }
        }

        return typeMap;
    }

    private static List<MethodDeclarationSyntax> CollectActiveMethods(CompilationUnitSyntax root)
    {
        var allMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var kernelMethods = allMethods.Where(static m => FuzzSyntaxHelpers.HasAttribute(m, "GpuKernel")).ToList();
        if (kernelMethods.Count == 0)
        {
            kernelMethods = allMethods;
        }

        var referencedHelperNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var km in kernelMethods)
        {
            foreach (var inv in km.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (inv.Expression is IdentifierNameSyntax id)
                {
                    referencedHelperNames.Add(id.Identifier.ValueText);
                }
            }
        }

        return allMethods
            .Where(m => kernelMethods.Contains(m) || referencedHelperNames.Contains(m.Identifier.ValueText))
            .ToList();
    }

    private static Dictionary<string, string> BuildMethodNormalizationMap(IReadOnlyList<MethodDeclarationSyntax> activeMethods)
    {
        var methodMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in activeMethods)
        {
            string mName = m.Identifier.ValueText;
            if (!methodMap.ContainsKey(mName))
            {
                methodMap[mName] = $"M{methodMap.Count}";
            }
        }

        return methodMap;
    }

    private static void AppendNormalizedMethod(
        StringBuilder sb,
        MethodDeclarationSyntax method,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap)
    {
        var cleanedMethod = FuzzAstNormalizer.StripDeadLocalsAndStores(method);
        var (varMap, localTypes) = FuzzAstNormalizer.BuildVariableNormalizationMap(cleanedMethod, typeMap);

        string methodToken = methodMap.TryGetValue(cleanedMethod.Identifier.ValueText, out string? mappedMethod)
            ? mappedMethod
            : "M0";
        string returnType = FuzzAstNormalizer.NormalizeTypeSyntax(cleanedMethod.ReturnType, typeMap);
        sb.Append(returnType).Append(' ').Append(methodToken).Append('(');

        for (int i = 0; i < cleanedMethod.ParameterList.Parameters.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            var p = cleanedMethod.ParameterList.Parameters[i];
            string pType = p.Type is not null ? FuzzAstNormalizer.NormalizeTypeSyntax(p.Type, typeMap) : "var";
            string pName = varMap[p.Identifier.ValueText];
            sb.Append(pType).Append(' ').Append(pName);
        }

        sb.Append("){");

        if (cleanedMethod.Body is not null)
        {
            foreach (var stmt in cleanedMethod.Body.Statements)
            {
                if (FuzzSyntaxHelpers.IsHoistedDefaultDeclaration(stmt) &&
                    !FuzzSyntaxHelpers.IsGidDeclaration(stmt) &&
                    !FuzzSyntaxHelpers.IsHashInitDeclaration(stmt))
                {
                    continue;
                }

                FuzzAstNormalizer.AppendNormalizedStatement(sb, stmt, varMap, methodMap, typeMap, localTypes);
            }
        }
        else if (cleanedMethod.ExpressionBody is not null)
        {
            sb.Append("return ");
            FuzzAstNormalizer.AppendNormalizedExpression(sb, cleanedMethod.ExpressionBody.Expression, varMap, methodMap, typeMap);
            sb.Append(';');
        }

        sb.Append('}');
    }

    private static int GetPathOrder(string path) => path.ToUpperInvariant() switch
    {
        "MANAGED" => 0,
        "NATIVE" or "CPU" => 1,
        "CUDA" or "GPU" => 2,
        _ => 3,
    };

    private static string NormalizePathName(string path) => path.ToUpperInvariant() switch
    {
        "MANAGED" => "Managed",
        "NATIVE" or "CPU" => "Native",
        "CUDA" or "GPU" => "Cuda",
        _ => path,
    };
}
