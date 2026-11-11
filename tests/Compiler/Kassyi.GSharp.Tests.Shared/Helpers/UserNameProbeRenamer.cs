#nullable enable
using System.Globalization;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Renames, at the symbol level, every parameter, local, label and local function declared in the GPU methods of a
/// compilation to a unique probe name, so that a test can find every place of the generated code that a user name
/// reached.
/// </summary>
/// <remarks>
/// A probe name is <see cref="Stem"/>, the probe number and <see cref="Terminator"/>, so no probe name contains another
/// one. The declarations and every reference that binds to them are renamed. A <c>nameof</c> that names one of them
/// becomes the string literal of its value first: its text is a value of the program, not a reference to the symbol,
/// and the value stays what it was.
/// </remarks>
public sealed class UserNameProbeRenamer
{
    /// <summary>The fixed start of every probe name.</summary>
    public const string Stem = "GsNameProbe";

    /// <summary>The fixed end of every probe name.</summary>
    public const char Terminator = 'Q';

    private readonly List<string> _originalNames;

    private UserNameProbeRenamer(Compilation compilation, List<string> originalNames)
    {
        Compilation = compilation;
        _originalNames = originalNames;
    }

    /// <summary>The compilation with every probed declaration and reference renamed.</summary>
    public Compilation Compilation { get; }

    /// <summary>The number of probed declarations.</summary>
    public int ProbeCount => _originalNames.Count;

    /// <summary>
    /// Renames the parameters, locals, labels and local functions declared in the methods of
    /// <paramref name="compilation"/> that <paramref name="isGpuMethod"/> selects.
    /// </summary>
    public static UserNameProbeRenamer Rename(Compilation compilation, Func<IMethodSymbol, bool> isGpuMethod)
    {
        var probes = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var originalNames = new List<string>();
        var gpuTrees = new List<(SyntaxTree Tree, SemanticModel Model)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            if (AddProbes(tree, model, isGpuMethod, probes, originalNames))
            {
                gpuTrees.Add((tree, model));
            }
        }

        return new UserNameProbeRenamer(RewriteTrees(compilation, gpuTrees, probes, originalNames), originalNames);
    }

    // Gives a probe name to every declaration in the GPU methods of the tree; whether the tree declares a GPU method
    private static bool AddProbes(SyntaxTree tree, SemanticModel model, Func<IMethodSymbol, bool> isGpuMethod,
        Dictionary<ISymbol, string> probes, List<string> originalNames)
    {
        var declaresGpuMethod = false;
        foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(method) is not { } symbol || !isGpuMethod(symbol))
            {
                continue;
            }

            declaresGpuMethod = true;
            foreach (var node in method.DescendantNodesAndSelf())
            {
                if (GetDeclaredIdentifier(node).IsKind(SyntaxKind.None) ||
                    model.GetDeclaredSymbol(node) is not { } declared ||
                    probes.ContainsKey(declared.OriginalDefinition))
                {
                    continue;
                }

                probes.Add(declared.OriginalDefinition,
                    Stem + originalNames.Count.ToString(CultureInfo.InvariantCulture) + Terminator);
                originalNames.Add(declared.Name);
            }
        }

        return declaresGpuMethod;
    }

    // [WHY] Only the trees that declare a GPU method are rewritten: a local, label or local function is referenced only
    // inside its method, and the parameters of a GPU method are named (named arguments) only by the GPU code that calls
    // it, which the scan transpiles from those same trees. A host call site elsewhere that names one would stop
    // binding, but the scan never transpiles it.
    private static Compilation RewriteTrees(Compilation compilation, List<(SyntaxTree Tree, SemanticModel Model)> trees,
        Dictionary<ISymbol, string> probes, List<string> originalNames)
    {
        var probedNames = originalNames.ToHashSet(StringComparer.Ordinal);
        var newRoots = new SyntaxNode[trees.Count];
        Parallel.For(0, trees.Count, i =>
        {
            var (tree, model) = trees[i];
            newRoots[i] = new Rewriter(probes, probedNames, model).Visit(tree.GetRoot());
        });

        var renamed = compilation;
        for (var i = 0; i < trees.Count; i++)
        {
            var tree = trees[i].Tree;
            renamed = renamed.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(newRoots[i], tree.Options));
        }

        return renamed;
    }

    /// <summary>The C# name that the probe numbered <paramref name="number"/> replaced.</summary>
    public string GetOriginalName(int number) => _originalNames[number];

    // The name a declaration introduces, as CppNameResolver collects it; default for any other node
    private static SyntaxToken GetDeclaredIdentifier(SyntaxNode node) =>
        node switch
        {
            ParameterSyntax p => p.Identifier,
            VariableDeclaratorSyntax v => v.Identifier,
            SingleVariableDesignationSyntax d => d.Identifier,
            ForEachStatementSyntax f => f.Identifier,
            LocalFunctionStatementSyntax l => l.Identifier,
            LabeledStatementSyntax l => l.Identifier,
            CatchDeclarationSyntax c => c.Identifier,
            _ => default
        };

    private sealed class Rewriter(
        Dictionary<ISymbol, string> probes,
        HashSet<string> probedNames,
        SemanticModel model) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (model.GetOperation(node) is INameOfOperation { ConstantValue: { HasValue: true, Value: string value } } &&
                node.ArgumentList.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Any(name => GetReferencedProbe(name) != null))
            {
                // "<value>"
                return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value))
                    .WithTriviaFrom(node);
            }

            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            GetReferencedProbe(node) is { } probe
                ? node.WithIdentifier(Rename(node.Identifier, probe))
                : base.VisitIdentifierName(node);

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (token.Parent is { } parent && GetDeclaredIdentifier(parent) == token &&
                probedNames.Contains(token.ValueText) &&
                GetProbe(model.GetDeclaredSymbol(parent)) is { } probe)
            {
                return Rename(token, probe);
            }

            return base.VisitToken(token);
        }

        // The spelling is checked first so that only the names spelled like a probed declaration are bound
        private string? GetReferencedProbe(IdentifierNameSyntax name) =>
            probedNames.Contains(name.Identifier.ValueText) ? GetProbe(model.GetSymbolInfo(name).Symbol) : null;

        private string? GetProbe(ISymbol? symbol) =>
            symbol != null && probes.TryGetValue(symbol.OriginalDefinition, out var probe) ? probe : null;

        private static SyntaxToken Rename(SyntaxToken token, string probe) =>
            SyntaxFactory.Identifier(token.LeadingTrivia, probe, token.TrailingTrivia);
    }
}
