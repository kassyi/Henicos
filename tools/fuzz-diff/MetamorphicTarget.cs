using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Finds the kernel a metamorphic rewrite works on and picks names for what the rewrite adds.
/// </summary>
internal static class MetamorphicTarget
{
    /// <summary>
    /// Returns the <c>[GpuKernel]</c> method named <paramref name="targetMethodName"/>, or the first kernel when the
    /// name is empty or not found.
    /// </summary>
    internal static MethodDeclarationSyntax? FindKernel(CompilationUnitSyntax root, string? targetMethodName)
    {
        var kernels = FuzzSyntaxHelpers.GetKernelMethods(root);
        if (kernels.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(targetMethodName))
        {
            foreach (var k in kernels)
            {
                if (string.Equals(k.Identifier.ValueText, targetMethodName, StringComparison.Ordinal))
                {
                    return k;
                }
            }
        }

        return kernels[0];
    }

    /// <summary>Collects every identifier spelled in <paramref name="root"/>.</summary>
    internal static HashSet<string> CollectIdentifiers(CompilationUnitSyntax root)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in root.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                set.Add(token.ValueText);
            }
        }

        return set;
    }

    /// <summary>Returns <c>{prefix}{n}</c> for the smallest <c>n</c> not in <paramref name="usedNames"/> and adds it.</summary>
    internal static string AllocateFreshIdentifier(string prefix, HashSet<string> usedNames)
    {
        for (int index = 0; ; index++)
        {
            string candidate = string.Create(CultureInfo.InvariantCulture, $"{prefix}{index}");
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
