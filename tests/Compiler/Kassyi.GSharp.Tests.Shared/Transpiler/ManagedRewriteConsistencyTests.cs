#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Pins <see cref="ManagedRewriteClassifier"/> to <see cref="ManagedExecutionRewriter"/> (#711, #578, #730): over every
/// device function of the test corpus, the rewriter changes the meaning of a body only where the classifier says it
/// does, so a managed-only rewrite that the classifier does not list cannot leave a function without its managed
/// version.
/// </summary>
/// <remarks>
/// A function that needs no managed version runs as the user's method on the managed path. Rewriting its body must then
/// give the body with its names qualified and nothing else, apart from the rewrites whose result the user's method
/// already gives: the body macros with a lambda (Kassyi.GSharp.Core runs them), <c>Gpu.InlinePtx</c> (unsupported
/// either way), the site key of a <c>SharedMemory&lt;T&gt;</c> (its caller-info constructor computes the same key) and
/// the length of an optional array.
/// </remarks>
[Category(TestCategory.Compiler)]
[Scenario(TestScenario.HappyPath)]
public class ManagedRewriteConsistencyTests
{
    [Fact]
    public void Test_FunctionWithoutManagedVersion_RewritesToItsQualifiedBody()
    {
        var compilation = MegaCompilationCache.Compilation;
        var checkedBodies = 0;
        var versions = 0;
        var violations = new List<string>();
        foreach (var (method, declaration, model) in EnumerateDeviceFunctions(compilation))
        {
            var body = declaration.GetBodyNode();
            if (body is null || SuspensionAnalysis.IsSuspendingFunction(method, compilation))
            {
                continue;
            }

            var rewritten = new ManagedExecutionRewriter(model, ManagedBodyKind.ManagedVersion,
                    new ManagedAccessorSet(method, compilation))
                .Visit(body)?.ToFullString();
            var qualified = ManagedNameQualifier.Qualify(body, model).ToFullString();
            if (ManagedVersionAnalysis.Needs(method, compilation))
            {
                versions++;
                if (rewritten == qualified)
                {
                    violations.Add($"{method}: needs a managed version, but the rewriter changes nothing");
                }

                continue;
            }

            if (HasEquivalentRewrite(body, model))
            {
                continue;
            }

            checkedBodies++;
            if (rewritten != qualified)
            {
                violations.Add($"{method}: the rewriter changes a body that ManagedRewriteClassifier does not classify");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));

        // The corpus holds both kinds: the operator macro / sequential sum functions and hundreds of plain functions
        Assert.True(versions >= 5, $"Only {versions} functions with a managed version in the corpus");
        Assert.True(checkedBodies >= 100, $"Only {checkedBodies} functions without one were compared");
    }

    // The direct rewrites of the classifier are the operator macro assignments and the float/double StackLinq sums
    [Fact]
    public void Test_EverySemanticRewrite_IsAnExpandedOperatorMacro()
    {
        var compilation = MegaCompilationCache.Compilation;
        var macros = 0;
        var sequentialSums = 0;
        foreach (var (_, declaration, model) in EnumerateDeviceFunctions(compilation))
        {
            foreach (var node in declaration.DescendantNodes())
            {
                if (!ManagedRewriteClassifier.IsSemanticRewrite(node, model))
                {
                    continue;
                }

                if (node is AssignmentExpressionSyntax assignment)
                {
                    macros++;
                    Assert.True(ManagedRewriteClassifier.TryGetOperatorMacro(assignment, model, out var macro));
                    Assert.NotNull(macro.TargetMethod);
                }
                else
                {
                    sequentialSums++;
                    var invocation = Assert.IsType<InvocationExpressionSyntax>(node);
                    Assert.True(ManagedRewriteClassifier.IsSequentialSum(invocation, model));
                }
            }
        }

        Assert.True(macros > 0);
        Assert.True(sequentialSums > 0);
    }

    [Fact]
    public void Test_SequentialSum_RewritesEveryFloatAndDoubleSumForm()
    {
        const string Source = """
            using Kassyi.GSharp.Core;
            using Kassyi.ZeroAlloc.Linq;

            public static partial class SequentialSumForms
            {
                [GpuDeviceFunction]
                public static double RewriteAllForms(float[] f, double[] d, int[] n)
                {
                    float f0 = f.AsStackEnumerable().Sum();
                    float f1 = f.AsStackEnumerable().Sum(x => x * 2f);
                    float f2 = f.AsStackEnumerable().Where(x => x > 0f).Select(x => x + 1f).Take(4).Sum();
                    float f3 = StackLinqExtensions.Sum(f.AsStackEnumerable());
                    float f4 = StackLinqExtensions.Sum(f.AsStackEnumerable(), x => x * 3f);
                    float f5 = StackLinqExtensions.Sum<ArrayEnumerator<float>>(f.AsStackEnumerable());

                    double d0 = d.AsStackEnumerable().Sum();
                    double d1 = d.AsStackEnumerable().Sum(x => x * 2.0);
                    double d2 = d.AsStackEnumerable().Where(x => x > 0.0).Select(x => x + 1.0).Take(4).Sum();
                    double d3 = StackLinqExtensions.Sum(d.AsStackEnumerable());
                    double d4 = StackLinqExtensions.Sum(d.AsStackEnumerable(), x => x * 3.0);

                    int i0 = n.AsStackEnumerable().Sum();
                    int i1 = n.AsStackEnumerable().Sum(x => x + 1);
                    float fp = f.AsStackEnumerable().AsParallel().Sum();

                    return f0 + f1 + f2 + f3 + f4 + f5 + d0 + d1 + d2 + d3 + d4 + i0 + i1 + fp;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(declaration));

        var rewritten = new ManagedExecutionRewriter(model, ManagedBodyKind.ManagedVersion,
                new ManagedAccessorSet(method, compilation))
            .Visit(declaration.GetBodyNode()!)!
            .ToFullString();

        // All 11 float/double StackEnumerable Sum calls (f0..f5, d0..d4) become SumSequential
        Assert.Equal(11, CountOccurrences(rewritten, ".SumSequential"));
        Assert.Contains("StackLinqExtensions.SumSequential<global::Kassyi.ZeroAlloc.Linq.ArrayEnumerator<float>>(", rewritten);

        // Only the 2 int Sum calls (i0, i1) and 1 ParallelStackEnumerable Sum call (fp) remain .Sum(
        Assert.Equal(3, CountOccurrences(rewritten, "StackLinqExtensions.Sum("));
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static IEnumerable<(IMethodSymbol Method, MethodDeclarationSyntax Declaration, SemanticModel Model)>
        EnumerateDeviceFunctions(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is { } method &&
                    method.HasAttribute(KnownAttributes.GpuDeviceFunction))
                {
                    yield return (method, declaration, model);
                }
            }
        }
    }

    private static bool HasEquivalentRewrite(SyntaxNode body, SemanticModel model) =>
        body.DescendantNodes().Any(node => node switch
        {
            InvocationExpressionSyntax invocation =>
                model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType.Name: "Gpu" } callee &&
                callee.Name is "ForUnroll" or "GridStrideLoop" or "SingleThread" or "InlinePtx",
            BaseObjectCreationExpressionSyntax creation =>
                model.GetTypeInfo(creation).Type is { Name: "SharedMemory" },
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" } access =>
                model.GetSymbolInfo(access.Expression).Symbol is IParameterSymbol
                {
                    Type: IArrayTypeSymbol, NullableAnnotation: NullableAnnotation.Annotated
                },
            _ => false
        });
}
