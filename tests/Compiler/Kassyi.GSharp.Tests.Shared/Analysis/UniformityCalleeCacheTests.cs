#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// The summary of a device function is cached per compilation, so a summary computed inside a recursion cycle (the
/// function still being analyzed further up seen as uniform) must not be cached: what a kernel learns of a call must not
/// depend on which kernel was analyzed first (#772 review item 4).
/// </summary>
[Category(TestCategory.Compiler)]
public class UniformityCalleeCacheTests
{
    private const string TestCase = "Analysis/RecursiveCalleeCases.cs";

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("KernelA", "KernelB")]
    [InlineData("KernelB", "KernelA")]
    public void Test_RecursionCycle_ReturnVarianceDoesNotDependOnOrder(string first, string second)
    {
        // A fresh compilation per order: the summaries are cached per compilation
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        Assert.True(ReturnsThreadVariant(compilation, first));
        Assert.True(ReturnsThreadVariant(compilation, second));
    }

    // Whether the call that the kernel stores (output[0] = <call>) returns a thread-variant value
    private static bool ReturnsThreadVariant(Compilation compilation, string kernel)
    {
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == kernel);
        var call = method.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        Assert.True(new MethodFlowGraph(model, method).TryGetUniformity(out var uniformity));
        return uniformity.IsThreadVariant(call);
    }
}
