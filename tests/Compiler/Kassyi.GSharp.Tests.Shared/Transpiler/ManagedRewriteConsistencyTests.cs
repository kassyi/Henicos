#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Pins <see cref="ManagedRewriteClassifier"/> to <see cref="ManagedExecutionRewriter"/> (#711, #578): over every device
/// function of the test corpus, the rewriter changes the meaning of a body only where the classifier says it does, so a
/// managed-only rewrite that the classifier does not list cannot leave a function without its managed version.
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

            var rewritten = new ManagedExecutionRewriter(model, ManagedBodyKind.ManagedVersion).Visit(body)?.ToFullString();
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

        // The corpus holds both kinds: the operator macro routing kernels and hundreds of plain functions
        Assert.True(versions >= 5, $"Only {versions} functions with a managed version in the corpus");
        Assert.True(checkedBodies >= 100, $"Only {checkedBodies} functions without one were compared");
    }

    // The direct rewrites of the classifier are exactly the operator macro assignments of the corpus
    [Fact]
    public void Test_EverySemanticRewrite_IsAnExpandedOperatorMacro()
    {
        var compilation = MegaCompilationCache.Compilation;
        var found = 0;
        foreach (var (_, declaration, model) in EnumerateDeviceFunctions(compilation))
        {
            foreach (var node in declaration.DescendantNodes())
            {
                if (!ManagedRewriteClassifier.IsSemanticRewrite(node, model))
                {
                    continue;
                }

                found++;
                var assignment = Assert.IsType<AssignmentExpressionSyntax>(node);
                Assert.True(ManagedRewriteClassifier.TryGetOperatorMacro(assignment, model, out var macro));
                Assert.NotNull(macro.TargetMethod);
            }
        }

        Assert.True(found > 0);
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
