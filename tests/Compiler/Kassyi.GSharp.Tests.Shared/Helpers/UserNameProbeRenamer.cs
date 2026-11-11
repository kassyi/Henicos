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
/// one. The declarations are the ones <see cref="CppNameResolver.GetDeclaredIdentifier"/> numbers; they and every
/// reference that binds to them are renamed. A <c>nameof</c> that names one of them becomes the string literal of its
/// value first: its text is a value of the program, not a reference to the symbol, and the value stays what it was.
/// </remarks>
public sealed class UserNameProbeRenamer
{
    /// <summary>The fixed start of every probe name.</summary>
    public const string Stem = "GsNameProbe";

    /// <summary>The fixed end of every probe name.</summary>
    public const char Terminator = 'Q';

    private readonly List<ISymbol> _symbols;

    private UserNameProbeRenamer(Compilation compilation, ImmutableArray<SyntaxTree> rewrittenTrees,
        List<ISymbol> symbols, ImmutableArray<string> addedErrors)
    {
        Compilation = compilation;
        RewrittenTrees = rewrittenTrees;
        _symbols = symbols;
        AddedErrors = addedErrors;
    }

    /// <summary>The compilation with every probed declaration and reference renamed.</summary>
    public Compilation Compilation { get; }

    /// <summary>
    /// The trees of <see cref="Compilation"/> that were rewritten: every tree that declares a GPU method.
    /// </summary>
    public ImmutableArray<SyntaxTree> RewrittenTrees { get; }

    /// <summary>
    /// The errors that the rewritten trees report and their original trees did not (<c>id at path:line</c>); empty when
    /// every reference was renamed with its declaration.
    /// </summary>
    public ImmutableArray<string> AddedErrors { get; }

    /// <summary>The number of probed declarations.</summary>
    public int ProbeCount => _symbols.Count;

    /// <summary>
    /// Renames the parameters, locals, labels and local functions declared in the methods of
    /// <paramref name="compilation"/> that <paramref name="isGpuMethod"/> selects.
    /// </summary>
    public static UserNameProbeRenamer Rename(Compilation compilation, Func<IMethodSymbol, bool> isGpuMethod)
    {
        var probes = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var symbols = new List<ISymbol>();
        var gpuTrees = new List<(SyntaxTree Tree, SemanticModel Model)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            if (AddProbes(tree, model, isGpuMethod, probes, symbols))
            {
                gpuTrees.Add((tree, model));
            }
        }

        var probedNames = symbols.Select(static symbol => symbol.Name).ToHashSet(StringComparer.Ordinal);
        var newTrees = new SyntaxTree[gpuTrees.Count];
        Parallel.For(0, gpuTrees.Count, i =>
        {
            var (tree, model) = gpuTrees[i];
            var root = new Rewriter(probes, probedNames, model).Visit(tree.GetRoot());
            newTrees[i] = tree.WithRootAndOptions(root, tree.Options);
        });

        var renamed = compilation;
        for (var i = 0; i < gpuTrees.Count; i++)
        {
            renamed = renamed.ReplaceSyntaxTree(gpuTrees[i].Tree, newTrees[i]);
        }

        var addedErrors = FindAddedErrors(gpuTrees, newTrees, renamed);
        return new UserNameProbeRenamer(renamed, [.. newTrees], symbols, addedErrors);
    }

    /// <summary>
    /// The declared symbol, in the original compilation, that the probe numbered <paramref name="number"/> renamed.
    /// </summary>
    public ISymbol GetSymbol(int number) => _symbols[number];

    // Gives a probe name to every declaration in the GPU methods of the tree; whether the tree declares a GPU method
    private static bool AddProbes(SyntaxTree tree, SemanticModel model, Func<IMethodSymbol, bool> isGpuMethod,
        Dictionary<ISymbol, string> probes, List<ISymbol> symbols)
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
                if (CppNameResolver.GetDeclaredIdentifier(node).IsKind(SyntaxKind.None) ||
                    model.GetDeclaredSymbol(node) is not { } declared ||
                    probes.ContainsKey(declared.OriginalDefinition))
                {
                    continue;
                }

                probes.Add(declared.OriginalDefinition,
                    Stem + symbols.Count.ToString(CultureInfo.InvariantCulture) + Terminator);
                symbols.Add(declared.OriginalDefinition);
            }
        }

        return declaresGpuMethod;
    }

    // [WHY] A partial rename (a reference that kept its old name) leaves C# that does not bind, which the transpiler may
    // still emit without a failure. Each rename replaces one token, so the lines do not move and the errors compare by
    // id and line.
    private static ImmutableArray<string> FindAddedErrors(List<(SyntaxTree Tree, SemanticModel Model)> originals,
        SyntaxTree[] newTrees, Compilation renamed)
    {
        var added = new ConcurrentBag<string>();
        Parallel.For(0, originals.Count, i =>
        {
            var before = ErrorKeys(originals[i].Model.GetDiagnostics()).ToHashSet(StringComparer.Ordinal);
            ErrorKeys(renamed.GetSemanticModel(newTrees[i]).GetDiagnostics())
                .Where(key => !before.Contains(key))
                .ToList()
                .ForEach(added.Add);
        });

        return [.. added.Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> ErrorKeys(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(ErrorKey);

    private static string ErrorKey(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        return $"{diagnostic.Id} at {span.Path}:{span.StartLinePosition.Line + 1}";
    }

    private sealed class Rewriter(
        Dictionary<ISymbol, string> probes,
        HashSet<string> probedNames,
        SemanticModel model) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (model.GetOperation(node) is INameOfOperation
                {
                    ConstantValue: { HasValue: true, Value: string value }
                } &&
                node.ArgumentList.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Any(name => GetReferencedProbe(name) != null))
            {
                // "<value>"
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(value)
                ).WithTriviaFrom(node);
            }

            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            GetReferencedProbe(node) is { } probe
                ? node.WithIdentifier(Rename(node.Identifier, probe))
                : base.VisitIdentifierName(node);

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (token.Parent is { } parent &&
                CppNameResolver.GetDeclaredIdentifier(parent) == token &&
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
            symbol != null && probes.TryGetValue(GetDeclaredParameter(symbol).OriginalDefinition, out var probe)
                ? probe
                : null;

        // A named argument of an extension method called in reduced form (x.Scale(factor: 3)) binds to the parameter
        // of the reduced method, which is not the declared one: the declared method has the receiver as parameter 0
        private static ISymbol GetDeclaredParameter(ISymbol symbol) =>
            symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { ReducedFrom: { } declared } } parameter
                ? declared.Parameters[parameter.Ordinal + 1]
                : symbol;

        private static SyntaxToken Rename(SyntaxToken token, string probe) =>
            SyntaxFactory.Identifier(token.LeadingTrivia, probe, token.TrailingTrivia);
    }
}
