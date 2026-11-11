#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// The inputs of the IR tests that cover every GPU method: each file under <c>TestCases/</c> in a compilation of its
/// own, as the snapshot generator compiles them, and the differential kernel files in the shared compilation of the
/// test suite.
/// </summary>
internal static class IrCoverageInputs
{
#pragma warning disable RS0030 // The test case files are parsed as the snapshot generator parses them
    private static readonly SyntaxTree s_globalUsings = CSharpSyntaxTree.ParseText(
        "global using System;" +
        "global using System.Collections.Generic;" +
        "global using System.IO;" +
        "global using System.Linq;" +
        "global using System.Runtime.CompilerServices;" +
        "global using System.Runtime.InteropServices;" +
        "global using Kassyi.GSharp.Core;",
        new CSharpParseOptions(LanguageVersion.Preview),
        path: "__GlobalUsings.g.cs");
#pragma warning restore RS0030

    /// <summary>
    /// Calls <paramref name="visit"/> with each compilation and the trees of it to cover; the test case files run in
    /// parallel.
    /// </summary>
    /// <param name="visit">Called with a compilation and the trees to cover in it.</param>
    /// <param name="everyKernelFile">
    /// Whether to cover every <c>*.cu.cs</c> file of the shared compilation instead of the differential kernels only.
    /// </param>
    /// <returns>The number of test case files and of shared kernel files.</returns>
    public static (int TestCaseFiles, int DifferentialFiles) ForEach(Action<Compilation, SyntaxTree[]> visit,
        bool everyKernelFile = false)
    {
        var testCases = Directory.GetFiles(
                RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "TestCases"), "*.cs",
                SearchOption.AllDirectories)
            .Where(static path => !path.EndsWith(".verified.cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Parallel.ForEach(testCases, path =>
        {
#pragma warning disable RS0030 // One compilation per file, as the snapshot generator compiles them
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path),
                new CSharpParseOptions(LanguageVersion.Preview), path: path);
#pragma warning restore RS0030
            visit(MegaCompilationCache.BaseCompilation.AddSyntaxTrees(s_globalUsings, tree), [tree]);
        });

        var shared = MegaCompilationCache.Compilation;
        var differential = shared.SyntaxTrees
            .Where(tree => everyKernelFile
                ? tree.FilePath.EndsWith(".cu.cs", StringComparison.Ordinal)
                : tree.FilePath.Contains(Path.DirectorySeparatorChar + "Differential" + Path.DirectorySeparatorChar) &&
                  tree.FilePath.EndsWith("Kernels.cu.cs", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(differential);
        visit(shared, differential);
        return (testCases.Length, differential.Length);
    }

    /// <summary>
    /// The <c>[GpuKernel]</c> and <c>[GpuDeviceFunction]</c> methods declared in <paramref name="trees"/> that have a
    /// body, with the syntax of the body and the built IR.
    /// </summary>
    public static IEnumerable<(SyntaxNode Body, IMethodSymbol Symbol, IrMethod Method)> GpuMethods(
        Compilation compilation, IEnumerable<SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is { } symbol && GpuExecutionSpace.IsGpuFunction(symbol) &&
                    BodyOf(declaration) is { } body &&
                    IrBuilder.TryBuild(symbol, compilation, CancellationToken.None) is { } method)
                {
                    yield return (body, symbol, method);
                }
            }
        }
    }

    /// <summary>
    /// The bodies of the members of the structs that G# emits for the device, declared in <paramref name="trees"/>:
    /// property and indexer accessors (also an expression-bodied property), constructors, operators and conversions.
    /// These are where a member reads its own struct through the implicit <see langword="this"/>.
    /// </summary>
    public static IEnumerable<(SyntaxNode Body, IMethodSymbol Symbol, IrMethod Method)> StructMembers(
        Compilation compilation, IEnumerable<SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (StructMemberBody(node, model) is not ({ } body, { } symbol) ||
                    symbol.ContainingType is not { } type || !GlobalCppNames.IsEmittedUserType(type) ||
                    IrBuilder.TryBuild(symbol, compilation, CancellationToken.None) is not { } method)
                {
                    continue;
                }

                yield return (body, symbol, method);
            }
        }
    }

    // The body and symbol of an accessor, an expression-bodied property, a constructor or an operator
    private static (SyntaxNode? Body, IMethodSymbol? Symbol) StructMemberBody(SyntaxNode node, SemanticModel model) =>
        node switch
        {
            AccessorDeclarationSyntax accessor => ((SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
                model.GetDeclaredSymbol(accessor) as IMethodSymbol),
            PropertyDeclarationSyntax { ExpressionBody: { } arrow } property =>
                (arrow, (model.GetDeclaredSymbol(property) as IPropertySymbol)?.GetMethod),
            IndexerDeclarationSyntax { ExpressionBody: { } arrow } indexer =>
                (arrow, (model.GetDeclaredSymbol(indexer) as IPropertySymbol)?.GetMethod),
            BaseMethodDeclarationSyntax member and not MethodDeclarationSyntax =>
                ((SyntaxNode?)member.Body ?? member.ExpressionBody, model.GetDeclaredSymbol(member) as IMethodSymbol),
            _ => (null, null)
        };

    private static SyntaxNode? BodyOf(MethodDeclarationSyntax declaration) =>
        (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody;

    /// <summary>Where a method is, for a test message.</summary>
    public static string Where(SyntaxTree tree, IMethodSymbol symbol) =>
        Path.GetFileName(tree.FilePath) + " " + symbol.ContainingType.Name + "." + symbol.Name;
}
