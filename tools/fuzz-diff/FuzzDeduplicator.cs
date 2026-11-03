using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
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
        string canonicalFailureKind = string.IsNullOrWhiteSpace(failureKind) ? "CellMismatch" : failureKind.AsSpan().Trim().ToString();
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

        var rawParts = FuzzSyntaxHelpers.SplitNonEmpty(failingPaths, ",|; \t");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>(rawParts.Count);
        foreach (string raw in rawParts)
        {
            string normalized = NormalizePathName(raw);
            if (seen.Add(normalized))
            {
                unique.Add(normalized);
            }
        }

        unique.Sort(static (a, b) =>
        {
            int cmp = GetPathOrder(a).CompareTo(GetPathOrder(b));
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(a, b);
        });

        string joined = string.Join(",", unique);
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

        using var writer = new SourceWriter();
        for (int i = 0; i < activeMethods.Count; i++)
        {
            if (i > 0)
            {
                writer.Append(';');
            }

            AppendNormalizedMethod(writer, activeMethods[i], methodMap, typeMap);
        }

        return writer.ToString();
    }

    private static Dictionary<string, string> BuildCustomTypeNormalizationMap(CompilationUnitSyntax root)
    {
        var typeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
        {
            if (node is BaseTypeDeclarationSyntax typeDecl &&
                typeDecl is StructDeclarationSyntax or EnumDeclarationSyntax)
            {
                string origName = typeDecl.Identifier.ValueText;
                if (!typeMap.ContainsKey(origName))
                {
                    typeMap[origName] = $"T{typeMap.Count}";
                }
            }
        }

        foreach (var node in root.DescendantNodes())
        {
            if (node is IdentifierNameSyntax id)
            {
                string name = id.Identifier.ValueText;
                if (name.StartsWith("FuzzEnum", StringComparison.Ordinal) && !typeMap.ContainsKey(name))
                {
                    typeMap[name] = $"T{typeMap.Count}";
                }
            }
        }

        return typeMap;
    }

    private static List<MethodDeclarationSyntax> CollectActiveMethods(CompilationUnitSyntax root)
    {
        var allMethods = new List<MethodDeclarationSyntax>();
        var kernelMethods = new List<MethodDeclarationSyntax>();
        foreach (var node in root.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax method)
            {
                allMethods.Add(method);
                if (FuzzSyntaxHelpers.HasAttribute(method, "GpuKernel"))
                {
                    kernelMethods.Add(method);
                }
            }
        }

        if (kernelMethods.Count == 0)
        {
            kernelMethods = allMethods;
        }

        var referencedHelperNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var km in kernelMethods)
        {
            foreach (var node in km.DescendantNodes())
            {
                if (node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax id })
                {
                    referencedHelperNames.Add(id.Identifier.ValueText);
                }
            }
        }

        var active = new List<MethodDeclarationSyntax>(allMethods.Count);
        foreach (var m in allMethods)
        {
            if (kernelMethods.Contains(m) || referencedHelperNames.Contains(m.Identifier.ValueText))
            {
                active.Add(m);
            }
        }

        return active;
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
        SourceWriter writer,
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
        writer.Append(returnType);
        writer.Append(' ');
        writer.Append(methodToken);
        writer.Append('(');

        for (int i = 0; i < cleanedMethod.ParameterList.Parameters.Count; i++)
        {
            if (i > 0)
            {
                writer.Append(',');
            }

            var p = cleanedMethod.ParameterList.Parameters[i];
            string pType = p.Type is not null ? FuzzAstNormalizer.NormalizeTypeSyntax(p.Type, typeMap) : "var";
            string pName = varMap[p.Identifier.ValueText];
            writer.Append(pType);
            writer.Append(' ');
            writer.Append(pName);
        }

        writer.Append("){");

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

                FuzzAstNormalizer.AppendNormalizedStatement(writer, stmt, varMap, methodMap, typeMap, localTypes);
            }
        }
        else if (cleanedMethod.ExpressionBody is not null)
        {
            writer.Append("return ");
            FuzzAstNormalizer.AppendNormalizedExpression(writer, cleanedMethod.ExpressionBody.Expression, varMap, methodMap, typeMap);
            writer.Append(';');
        }

        writer.Append('}');
    }

    private static int GetPathOrder(string path)
    {
        if (string.Equals(path, "Managed", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(path, "Native", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "Cpu", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(path, "Cuda", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "Gpu", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private static string NormalizePathName(string path)
    {
        if (string.Equals(path, "Managed", StringComparison.OrdinalIgnoreCase))
        {
            return "Managed";
        }

        if (string.Equals(path, "Native", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "Cpu", StringComparison.OrdinalIgnoreCase))
        {
            return "Native";
        }

        if (string.Equals(path, "Cuda", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "Gpu", StringComparison.OrdinalIgnoreCase))
        {
            return "Cuda";
        }

        return path;
    }
}
