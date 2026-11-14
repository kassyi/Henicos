#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
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
/// already gives: the body macros with a lambda (Kassyi.GSharp.Core runs them), the site key of a
/// <c>SharedMemory&lt;T&gt;</c> (its caller-info constructor computes the same key), the length of an optional array,
/// the calls that C# removes (<c>RemovedCalls</c>), which the user's method does not make either, the arm that C# adds
/// to a switch expression that is not exhaustive, the <c>in</c> of a variable passed to a <c>ref readonly</c>
/// parameter without a keyword, and the value of a caller-info parameter that a call omits (the value C# passes at the
/// same call).
/// </remarks>
[Category(TestCategory.Compiler)]
[Scenario(TestScenario.HappyPath)]
[Collection(TestCollections.CorpusScan)]
public class ManagedRewriteConsistencyTests
{
    [Fact]
    public void Rewrite_FunctionWithoutManagedVersion_RewritesToItsQualifiedBody()
    {
        var compilation = RoslynTestUtility.SharedCompilation;
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

            var rewrittenNode = new ManagedExecutionRewriter(model, ManagedBodyKind.ManagedVersion,
                    new ManagedAccessorSet(method, compilation))
                .Visit(body);
            var qualifiedNode = ManagedNameQualifier.Qualify(body, model);
            var isEquivalent = SyntaxFactory.AreEquivalent(rewrittenNode, qualifiedNode);
            if (ManagedVersionAnalysis.Needs(method, compilation))
            {
                versions++;
                if (isEquivalent)
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
            if (!isEquivalent)
            {
                violations.Add($"{method}: the rewriter changes a body that ManagedRewriteClassifier does not classify");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));

        // The corpus holds both kinds: the operator macro / sequential sum functions and hundreds of plain functions
        Assert.True(versions >= 5, $"Only {versions} functions with a managed version in the corpus");
        Assert.True(checkedBodies >= 100, $"Only {checkedBodies} functions without one were compared");
    }

    // The direct rewrites of the classifier are the operator macro assignments, the float/double StackLinq sums,
    // Gpu.InlinePtx and the int or long divisions that may divide the minimum by -1
    [Fact]
    public void Rewrite_SemanticRewriteCandidate_IsAListedRewrite()
    {
        var compilation = RoslynTestUtility.SharedCompilation;
        var kinds = new Dictionary<string, int>();
        foreach (var (_, declaration, model) in EnumerateDeviceFunctions(compilation))
        {
            foreach (var node in declaration.DescendantNodes())
            {
                if (ManagedRewriteClassifier.IsSemanticRewrite(node, model))
                {
                    var kind = ClassifyRewrite(node, model);
                    kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                }
            }
        }

        Assert.True(kinds.GetValueOrDefault("macro") > 0);
        Assert.True(kinds.GetValueOrDefault("sum") > 0);
        Assert.True(kinds.GetValueOrDefault("division") > 0);
    }

    // Which listed rewrite a node the classifier reports is; fails for a node that is none of them
    private static string ClassifyRewrite(SyntaxNode node, SemanticModel model)
    {
        if (ManagedDivision.Plan(node, model) is not null)
        {
            return "division";
        }

        if (node is AssignmentExpressionSyntax assignment)
        {
            Assert.True(ManagedRewriteClassifier.TryGetOperatorMacro(assignment, model, out var macro));
            Assert.NotNull(macro.TargetMethod);
            return "macro";
        }

        var invocation = Assert.IsType<InvocationExpressionSyntax>(node);
        if (ManagedRewriteClassifier.IsInlinePtx(invocation, model))
        {
            return "ptx";
        }

        Assert.True(ManagedRewriteClassifier.IsSequentialSum(invocation, model));
        return "sum";
    }

    [Fact]
    public void Rewrite_SequentialSum_RewritesEveryFloatAndDoubleSumForm()
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

        var body = declaration.GetBodyNode();
        Assert.NotNull(body);
        var visited = new ManagedExecutionRewriter(model, ManagedBodyKind.ManagedVersion,
                new ManagedAccessorSet(method, compilation))
            .Visit(body);
        Assert.NotNull(visited);

        var invocations = visited.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        var floatEnumerator = SyntaxFactory.ParseTypeName("global::Kassyi.ZeroAlloc.Linq.ArrayEnumerator<float>");

        // All 11 float/double StackEnumerable Sum calls (f0..f5, d0..d4) become SumSequential
        Assert.Equal(11, invocations.Count(static i =>
            Callee(i) is (not null, { Identifier.ValueText: "SumSequential" })));
        Assert.Contains(invocations, i => Callee(i) is (var receiver, GenericNameSyntax
        {
            Identifier.ValueText: "SumSequential",
            TypeArgumentList.Arguments: [var argument]
        }) && IsStackLinqExtensions(receiver) && SyntaxFactory.AreEquivalent(argument, floatEnumerator));

        // Only the 2 int Sum calls (i0, i1) and 1 ParallelStackEnumerable Sum call (fp) remain StackLinqExtensions.Sum(
        Assert.Equal(3, invocations.Count(static i =>
            Callee(i) is (var receiver, IdentifierNameSyntax { Identifier.ValueText: "Sum" }) &&
            IsStackLinqExtensions(receiver)));
    }

    // <receiver>.<name>(...) as a member access or a qualified name; <name>(...) has no receiver
    private static (ExpressionSyntax? Receiver, SimpleNameSyntax? Name) Callee(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => (access.Expression, access.Name),
            QualifiedNameSyntax qualified => (qualified.Left, qualified.Right),
            SimpleNameSyntax simple => (null, simple),
            _ => (null, null)
        };

    // StackLinqExtensions, with or without its namespace
    private static bool IsStackLinqExtensions(ExpressionSyntax? receiver) =>
        receiver switch
        {
            MemberAccessExpressionSyntax access => access.Name,
            QualifiedNameSyntax qualified => qualified.Right,
            SimpleNameSyntax simple => simple,
            _ => null
        } is { Identifier.ValueText: "StackLinqExtensions" };

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
            InvocationExpressionSyntax invocation when RemovedCalls.IsRemoved(invocation, model) => true,
            InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax when OmitsCallerInfo(node, model) => true,
            InvocationExpressionSyntax invocation =>
                model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType.Name: "Gpu" } callee &&
                callee.Name is "ForUnroll" or "GridStrideLoop" or "SingleThread",
            BaseObjectCreationExpressionSyntax creation =>
                model.GetTypeInfo(creation).Type is { Name: "SharedMemory" },
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" } access =>
                model.GetSymbolInfo(access.Expression).Symbol is IParameterSymbol
                {
                    Type: IArrayTypeSymbol, NullableAnnotation: NullableAnnotation.Annotated
                },
            SwitchExpressionSyntax switchExpression =>
                model.GetOperation(switchExpression) is ISwitchExpressionOperation { IsExhaustive: false },
            ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.None } argument =>
                model.GetOperation(argument.Expression) is
                {
                    Parent: IArgumentOperation { Parameter.RefKind: RefKind.RefReadOnlyParameter }
                },
            _ => false
        });

    private static bool OmitsCallerInfo(SyntaxNode call, SemanticModel model) =>
        model.GetSymbolInfo(call).Symbol is IMethodSymbol method &&
        method.Parameters.Any(parameter => CallerInfoArgument.GetValue(parameter, call, model) is not null);
}
