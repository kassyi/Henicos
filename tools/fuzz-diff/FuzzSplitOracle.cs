using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Builds the oracle twins of split kernels: kernels and device functions that reach a warp shuffle or a block barrier.
/// </summary>
/// <remarks>
/// <para>
/// The sequential oracle runs a kernel's C# once per thread, where <c>Warp.Shuffle*</c> throws and
/// <c>Gpu.SyncThreads</c> does nothing. A split kernel is therefore generated so that its result does not depend on
/// another thread: every shuffle reads the calling lane (<c>ShuffleSync(m, v, ThreadIndex.X &amp; 31)</c>, or a delta
/// or lane mask of zero) with a full mask, no thread reads what another thread writes, and every split point sits in
/// control flow that all threads take the same way. The GPU then returns <c>v</c> for each shuffle, and a barrier
/// changes no value, so the per-thread C# with each shuffle replaced by its value argument and each barrier removed is
/// the definition of the result.
/// </para>
/// <para>
/// The twin of a method is that rewrite under the name <c>&lt;name&gt;_Oracle</c>, without attributes, calling the
/// twins of the split methods it calls. The rewrite checks the conditions it relies on and refuses a method that breaks
/// one (<see cref="Validate"/>).
/// </para>
/// </remarks>
public static class FuzzSplitOracle
{
    /// <summary>The suffix of an oracle twin's name.</summary>
    public const string OracleSuffix = "_Oracle";

    /// <summary>The only mask a generated shuffle passes.</summary>
    public const string FullMaskLiteral = "0xFFFFFFFFu";

    /// <summary>The source lane of a generated <c>Warp.ShuffleSync</c>: the calling lane of a one-dimensional
    /// block.</summary>
    public const string SelfLaneExpression = "ThreadIndex.X & 31";

    /// <summary>The launch of a split kernel: two blocks of two full warps each, so every lane of every warp
    /// runs.</summary>
    public const string LaunchShapeExpression = "LaunchShapes.Grid1D(2, 64)";

    private static readonly string[] s_shuffleNames =
    [
        "ShuffleSync",
        "ShuffleXorSync",
        "ShuffleUpSync",
        "ShuffleDownSync",
    ];

    /// <summary>Returns the name of the oracle twin of <paramref name="methodName"/>.</summary>
    public static string OracleName(string methodName) => methodName + OracleSuffix;

    /// <summary>
    /// Returns the names of the GPU methods in <paramref name="gpuMethods"/> that reach a shuffle or a barrier,
    /// directly or through another method of the list.
    /// </summary>
    public static HashSet<string> CollectSplitMethodNames(IReadOnlyList<MethodDeclarationSyntax> gpuMethods)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in gpuMethods)
        {
            if (ContainsSplitIntrinsic(method))
            {
                names.Add(method.Identifier.ValueText);
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var method in gpuMethods)
            {
                if (!names.Contains(method.Identifier.ValueText) && CallsAny(method, names))
                {
                    names.Add(method.Identifier.ValueText);
                    changed = true;
                }
            }
        }

        return names;
    }

    /// <summary>Returns whether <paramref name="kernel"/> is a split kernel of <paramref name="root"/>.</summary>
    public static bool IsSplitKernel(MethodDeclarationSyntax kernel, CompilationUnitSyntax root)
    {
        var gpuMethods = FuzzSyntaxHelpers.GetDeviceFunctionMethods(root);
        if (!ContainsMethod(gpuMethods, kernel))
        {
            gpuMethods.Add(kernel);
        }

        return CollectSplitMethodNames(gpuMethods).Contains(kernel.Identifier.ValueText);
    }

    /// <summary>
    /// Returns why the twin of <paramref name="method"/> would not be the definition of its result, or <see
    /// langword="null"/> when it would: a shuffle that does not read the calling lane with a full mask, a split point
    /// under control flow that threads may take differently, or a jump that lets threads leave a loop with a split
    /// point.
    /// </summary>
    public static string? Validate(MethodDeclarationSyntax method, IReadOnlySet<string> splitMethodNames) =>
        FuzzSplitOracleGuard.Validate(method, splitMethodNames);

    /// <summary>
    /// Builds the twin of <paramref name="method"/>; throws <see cref="InvalidOperationException"/> when <see
    /// cref="Validate"/> refuses it.
    /// </summary>
    public static MethodDeclarationSyntax BuildTwin(
        MethodDeclarationSyntax method,
        IReadOnlySet<string> splitMethodNames)
    {
        if (Validate(method, splitMethodNames) is { } reason)
        {
            throw new InvalidOperationException(
                $"Cannot build the oracle twin of '{method.Identifier.ValueText}': {reason}.");
        }

        var rewritten = (MethodDeclarationSyntax)(new TwinRewriter(splitMethodNames).Visit(method)
            ?? throw new InvalidOperationException("The twin rewriter removed the method."));
        return rewritten
            .WithAttributeLists(default)
            .WithIdentifier(SyntaxFactory.Identifier(OracleName(method.Identifier.ValueText)))
            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
    }

    /// <summary>
    /// Returns <paramref name="source"/> with the twins of its split methods, built afresh: earlier twins are removed
    /// and a second <c>partial</c> declaration of the class that declares the first split method holds the new ones.
    /// Throws <see cref="InvalidOperationException"/> when a split method cannot have a twin.
    /// </summary>
    public static string WithOracleTwins(string source)
    {
        var root = RemoveTwins(FuzzSyntaxHelpers.ParseRoot(source), out bool removedAny);
        var gpuMethods = new List<MethodDeclarationSyntax>();
        gpuMethods.AddRange(FuzzSyntaxHelpers.GetDeviceFunctionMethods(root));
        gpuMethods.AddRange(FuzzSyntaxHelpers.GetKernelMethods(root));
        var splitNames = CollectSplitMethodNames(gpuMethods);
        string baseSource = removedAny ? FuzzSyntaxHelpers.RenderFullNode(root) : source;
        if (splitNames.Count == 0)
        {
            return baseSource;
        }

        string twins = RenderTwins(gpuMethods, splitNames, out string className);
        using var writer = new SourceWriter();
        writer.AppendLine(baseSource.AsSpan().TrimEnd().ToString());
        writer.AppendLine();
        writer.AppendLine("// Oracle twins of the split kernels (FuzzSplitOracle).");
        using (writer.Scope($"public static partial class {className}"))
        {
            writer.Append(twins);
            writer.AppendLine();
        }

        return writer.ToString();
    }

    // The twins of the split methods, left-aligned, and the class that declares the first of them
    private static string RenderTwins(
        List<MethodDeclarationSyntax> gpuMethods,
        HashSet<string> splitNames,
        out string className)
    {
        string? declaringClass = null;
        using var twinWriter = new SourceWriter();
        foreach (var method in gpuMethods)
        {
            if (!splitNames.Contains(method.Identifier.ValueText))
            {
                continue;
            }

            declaringClass ??= (method.Parent as ClassDeclarationSyntax)?.Identifier.ValueText;
            AppendLeftAligned(twinWriter, FuzzSyntaxHelpers.RenderFullNode(BuildTwin(method, splitNames)));
            twinWriter.AppendLine();
        }

        className = declaringClass ?? throw new InvalidOperationException("A split method is not declared in a class.");
        return twinWriter.ToString().AsSpan().TrimEnd().ToString();
    }

    /// <summary>Returns whether <paramref name="invocation"/> calls <c>Gpu.SyncThreads</c>.</summary>
    internal static bool IsBarrier(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "Gpu" },
            Name.Identifier.ValueText: "SyncThreads",
        };

    /// <summary>Returns whether <paramref name="invocation"/> calls one of the four <c>Warp.Shuffle*Sync</c>
    /// methods.</summary>
    internal static bool IsShuffle(InvocationExpressionSyntax invocation, out string name)
    {
        name = string.Empty;
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" },
            } access)
        {
            return false;
        }

        foreach (string shuffle in s_shuffleNames)
        {
            if (string.Equals(access.Name.Identifier.ValueText, shuffle, StringComparison.Ordinal))
            {
                name = shuffle;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsMethod(List<MethodDeclarationSyntax> methods, MethodDeclarationSyntax method)
    {
        foreach (var candidate in methods)
        {
            if (string.Equals(candidate.Identifier.ValueText, method.Identifier.ValueText, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSplitIntrinsic(MethodDeclarationSyntax method)
    {
        foreach (var node in method.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation &&
                (IsBarrier(invocation) || IsShuffle(invocation, out _)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CallsAny(SyntaxNode node, IReadOnlySet<string> names)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            if (descendant is InvocationExpressionSyntax invocation &&
                FuzzSyntaxHelpers.GetInvokedMethodName(invocation) is { } name &&
                names.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static CompilationUnitSyntax RemoveTwins(CompilationUnitSyntax root, out bool removedAny)
    {
        var twins = new List<SyntaxNode>();
        foreach (var node in root.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax method &&
                method.Identifier.ValueText.EndsWith(OracleSuffix, StringComparison.Ordinal) &&
                method.AttributeLists.Count == 0)
            {
                twins.Add(method);
            }
        }

        removedAny = twins.Count > 0;
        if (!removedAny)
        {
            return root;
        }

        var stripped = root.RemoveNodes(twins, SyntaxRemoveOptions.KeepNoTrivia) ?? root;
        var emptyClasses = new List<SyntaxNode>();
        foreach (var node in stripped.DescendantNodes())
        {
            if (node is ClassDeclarationSyntax { Members.Count: 0 } emptyClass &&
                emptyClass.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                emptyClasses.Add(emptyClass);
            }
        }

        return emptyClasses.Count == 0
            ? stripped
            : stripped.RemoveNodes(emptyClasses, SyntaxRemoveOptions.KeepNoTrivia) ?? stripped;
    }

    private static void AppendLeftAligned(SourceWriter writer, string text)
    {
        foreach (string line in FuzzSyntaxHelpers.SplitNonEmpty(text, "\r\n"))
        {
            ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
            if (!trimmed.IsEmpty)
            {
                writer.AppendLine(trimmed.ToString());
            }
        }
    }

    /// <summary>
    /// Replaces each shuffle by its value argument, removes each barrier and renames each call of a split method to its
    /// twin.
    /// </summary>
    private sealed class TwinRewriter(IReadOnlySet<string> splitMethodNames) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (IsShuffle(node, out _))
            {
                var value = (ExpressionSyntax)(Visit(node.ArgumentList.Arguments[1].Expression)
                    ?? throw new InvalidOperationException("The twin rewriter removed a shuffle value."));
                return SyntaxFactory.ParenthesizedExpression(value.WithoutTrivia()).WithTriviaFrom(node);
            }

            var visited = (InvocationExpressionSyntax)(base.VisitInvocationExpression(node)
                ?? throw new InvalidOperationException("The twin rewriter removed a call."));
            if (FuzzSyntaxHelpers.GetInvokedMethodName(node) is not { } name || !splitMethodNames.Contains(name))
            {
                return visited;
            }

            var twinName = SyntaxFactory.IdentifierName(OracleName(name));
            return visited.Expression switch
            {
                IdentifierNameSyntax id => visited.WithExpression(twinName.WithTriviaFrom(id)),
                MemberAccessExpressionSyntax access =>
                    visited.WithExpression(access.WithName(twinName.WithTriviaFrom(access.Name))),
                _ => throw new InvalidOperationException($"Unsupported call form of split method '{name}'."),
            };
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) =>
            node.Expression is InvocationExpressionSyntax invocation && IsBarrier(invocation)
                ? SyntaxFactory.EmptyStatement().WithTriviaFrom(node)
                : base.VisitExpressionStatement(node);
    }
}
