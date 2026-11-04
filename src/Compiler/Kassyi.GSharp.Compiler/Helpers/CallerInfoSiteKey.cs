namespace Kassyi.GSharp.Compiler.Helpers;

using Kassyi.GSharp.Hashing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Computes site keys matching Roslyn's caller-info attributes (<see cref="System.Runtime.CompilerServices.CallerFilePathAttribute"/>
/// and <see cref="System.Runtime.CompilerServices.CallerLineNumberAttribute"/>) under #line directives and compilation PathMap (#543).
/// </summary>
internal static class CallerInfoSiteKey
{
    public const string ConstructorMemberName = ".ctor";

    /// <summary>
    /// Returns the display path for a syntax span, respecting #line directives and compilation PathMap.
    /// Matches Roslyn's internal <c>SyntaxTree.GetDisplayPath</c>.
    /// </summary>
    public static string GetDisplayPath(SyntaxTree tree, TextSpan span, SourceReferenceResolver? resolver)
    {
        var mappedSpan = tree.GetMappedLineSpan(span);
        var path = mappedSpan.Path;
        if (resolver is null || string.IsNullOrEmpty(path))
        {
            return path;
        }

        var baseFilePath = mappedSpan.HasMappedPath ? tree.FilePath : null;
        return resolver.NormalizePath(path, baseFilePath) ?? path;
    }

    /// <summary>
    /// Returns the 1-based display line number for a syntax span, respecting #line directives.
    /// Matches Roslyn's internal <c>SyntaxTree.GetDisplayLineNumber</c>.
    /// </summary>
    public static int GetDisplayLineNumber(SyntaxTree tree, TextSpan span)
    {
        return tree.GetMappedLineSpan(span).StartLinePosition.Line + 1;
    }

    /// <summary>
    /// Computes the site key for a <c>SharedMemory&lt;T&gt;</c> creation in a constructor using FNV-1a 64.
    /// Matches <c>SharedMemory.ComputeSiteKey(filePath, lineNumber, ".ctor")</c> at runtime.
    /// </summary>
    public static ulong ComputeConstructorSiteKey(
        SyntaxTree tree,
        TextSpan span,
        SourceReferenceResolver? resolver)
    {
        var path = GetDisplayPath(tree, span, resolver);
        var line = GetDisplayLineNumber(tree, span);
        return ComputeConstructorSiteKey(path, line);
    }

    /// <summary>
    /// Computes the site key for a constructor call given resolved file path and line number using FNV-1a 64.
    /// </summary>
    public static ulong ComputeConstructorSiteKey(string filePath, int lineNumber)
    {
        var hash = new Fnv1A64();
        hash.Append(filePath);
        hash.Append(unchecked((uint)lineNumber));
        hash.Append(ConstructorMemberName);
        return hash.Value;
    }
}
