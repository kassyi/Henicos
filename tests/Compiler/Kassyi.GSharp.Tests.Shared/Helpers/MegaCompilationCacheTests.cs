#pragma warning disable RS0030 // Do not use banned APIs

namespace Kassyi.GSharp.Tests.Shared.Helpers;

public class MegaCompilationCacheTests
    : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Stress)]
    public void GetSourceContext_ConcurrentAccess_ReturnsSameInstances()
    {
        var thisFilePath = GetThisFilePath();
        var testCasePath = GetTestCasePath("MacroExpansion.cu.cs");
        var (expectedComp, expectedTree) = MegaCompilationCache.GetSourceContext(thisFilePath);
        var (expectedCaseComp, expectedCaseTree) = MegaCompilationCache.GetSourceContext(testCasePath);
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 300, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            try
            {
                var (comp, tree) = MegaCompilationCache.GetSourceContext(thisFilePath);
                Assert.Same(expectedComp, comp);
                Assert.Same(expectedTree, tree);

                var (caseComp, caseTree) = MegaCompilationCache.GetSourceContext(testCasePath);
                Assert.Same(expectedCaseComp, caseComp);
                Assert.Same(expectedCaseTree, caseTree);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void GetSourceContext_SharedSource_UsesSingleCompilation()
    {
        var (comp, tree) = MegaCompilationCache.GetSourceContext(GetThisFilePath());

        Assert.Same(MegaCompilationCache.Compilation, comp);
        Assert.Contains(tree, comp.SyntaxTrees);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void GetSourceContext_TestCase_IsIsolatedPerFile()
    {
        var (compA, treeA) = MegaCompilationCache.GetSourceContext(GetTestCasePath("MacroExpansion.cu.cs"));
        var (compB, treeB) = MegaCompilationCache.GetSourceContext(GetTestCasePath("AddKernel.cu.cs"));

        Assert.NotSame(compA, compB);
        Assert.NotSame(MegaCompilationCache.Compilation, compA);

        // Global usings tree + the test case itself, nothing else leaks in.
        Assert.Equal(2, compA.SyntaxTrees.Count());
        Assert.Contains(treeA, compA.SyntaxTrees);
        Assert.DoesNotContain(treeB, compA.SyntaxTrees);
        Assert.DoesNotContain(treeA, MegaCompilationCache.Compilation.SyntaxTrees);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void Compilation_HasNoDuplicateDefinitions()
    {
        // Guards the isolated-file rules against drift from Kassyi.GSharp.Tests.Shared.projitems:
        // a test-case input leaking into the shared compilation shows up as a duplicate definition.
        var duplicates = MegaCompilationCache.Compilation.GetDeclarationDiagnostics()
            .Where(d => d.Id is "CS0101" or "CS0102")
            .Select(d => d.ToString())
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void BaseCompilation_IsNotNullAndHasUnsafeAllowed()
    {
        var baseComp = MegaCompilationCache.BaseCompilation;
        Assert.NotNull(baseComp);
        Assert.True(baseComp.Options.AllowUnsafe);
    }

    private static string GetThisFilePath([CallerFilePath] string path = "") => path;

    private static string GetTestCasePath(string fileName, [CallerFilePath] string path = "") =>
        Path.Combine(RoslynTestUtility.GetTestCasesDirectory(path), fileName);
}
