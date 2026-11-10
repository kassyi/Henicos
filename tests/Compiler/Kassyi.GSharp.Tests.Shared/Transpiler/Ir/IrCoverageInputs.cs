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
    /// <returns>The number of test case files and of differential files.</returns>
    public static (int TestCaseFiles, int DifferentialFiles) ForEach(Action<Compilation, SyntaxTree[]> visit)
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
            .Where(static tree => tree.FilePath.Contains(Path.DirectorySeparatorChar + "Differential" +
                                                         Path.DirectorySeparatorChar) &&
                                  tree.FilePath.EndsWith("Kernels.cu.cs", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(differential);
        visit(shared, differential);
        return (testCases.Length, differential.Length);
    }

    /// <summary>
    /// The <c>[GpuKernel]</c> and <c>[GpuDeviceFunction]</c> methods declared in <paramref name="trees"/> that have a
    /// body, with their declarations and built IR.
    /// </summary>
    public static IEnumerable<(MethodDeclarationSyntax Declaration, IMethodSymbol Symbol, IrMethod Method)> GpuMethods(
        Compilation compilation, IEnumerable<SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is { } symbol && GpuExecutionSpace.IsGpuFunction(symbol) &&
                    IrBuilder.TryBuild(symbol, compilation, CancellationToken.None) is { } method)
                {
                    yield return (declaration, symbol, method);
                }
            }
        }
    }

    /// <summary>Where a method is, for a test message.</summary>
    public static string Where(SyntaxTree tree, IMethodSymbol symbol) =>
        Path.GetFileName(tree.FilePath) + " " + symbol.ContainingType.Name + "." + symbol.Name;
}
