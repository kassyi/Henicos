using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Whether C# removes a call (<c>RemovedCalls</c>): partial methods without an implementation and
/// <c>[Conditional]</c> methods against the parse options and the <c>#define</c> / <c>#undef</c> of the file; and that
/// lowering leaves nothing of a removed call behind (<c>RemovedCallEraser</c>).
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class RemovedCallsTests
{
    private static readonly Lazy<(Compilation Compilation, SyntaxTree Tree)> s_case = new(CreateCase);

    private static (Compilation Compilation, SyntaxTree Tree) CreateCase()
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview,
            preprocessorSymbols: ["OPTION_ON", "UNDEFINED_HERE"]);
        var tree = CSharpSyntaxTree.ParseText(
            RoslynTestUtility.ReadTestCase("Transpiler/RemovedCallCases.cs"), options, path: "RemovedCallCases.cs");
        var compilation = CSharpCompilation.Create("RemovedCallCases")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(tree);
        return (compilation, tree);
    }

    private static MethodDeclarationSyntax FindMethod(string name) =>
        s_case.Value.Tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.ValueText == name && (m.Body != null || m.ExpressionBody != null));

    private static SemanticModel Model => s_case.Value.Compilation.GetSemanticModel(s_case.Value.Tree);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Case_Compiles_WithoutErrors()
    {
        var errors = s_case.Value.Compilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("CallsNever", true)]
    [InlineData("CallsOptionOn", false)]
    [InlineData("CallsUndefinedHere", true)]
    [InlineData("CallsFileOn", false)]
    [InlineData("CallsInactiveDefine", true)]
    [InlineData("CallsEither", false)]
    [InlineData("CallsNoImplementation", true)]
    [InlineData("CallsWithImplementation", false)]
    [InlineData("CallsPlain", false)]
    [InlineData("CallsDebugAssert", true)]
    public void IsRemoved_CallOfCase_FollowsCSharp(string caller, bool expected)
    {
        var call = FindMethod(caller).DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        Assert.Equal(expected, RemovedCalls.IsRemoved(call, Model));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_BarrierOrShuffleOnlyInRemovedArgument_DoesNotSuspend()
    {
        Assert.True(Suspends("BumpAfterBarrier"));
        Assert.False(Suspends("CallsOnlyRemovedBarrier"));
        Assert.False(Suspends("ShufflesOnlyInRemovedCall"));

        static bool Suspends(string name) =>
            SuspensionAnalysis.IsSuspendingFunction(
                Model.GetDeclaredSymbol<IMethodSymbol>(FindMethod(name)) ?? throw new InvalidOperationException(name),
                s_case.Value.Compilation
            );
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Lower_RemovedCallsWithSplitArguments_LeaveNoSplitShuffleOrArgument()
    {
        var method = FindMethod("SplitArgumentsKernel");
        var model = Model;

        var lowered = AstLoweringPipeline.Lower(method, model, model.GetDeclaredSymbol<IMethodSymbol>(method), false);

        Assert.Empty(lowered.CallSites);
        Assert.Equal(0, lowered.WarpSiteCount);
        var text = lowered.LoweredBody.ToFullString();
        Assert.DoesNotContain("BumpAfterBarrier", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Shuffle", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Trace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_seq", text, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_spill", text, StringComparison.Ordinal);
        Assert.Contains("k++)", text, StringComparison.Ordinal);
        Assert.Contains("Local(i)", text, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Erase_RemovedCalls_KeepTheOriginalSpans()
    {
        var method = FindMethod("SplitArgumentsKernel");
        var model = Model;

        var lowered = AstLoweringPipeline.Lower(method, model, model.GetDeclaredSymbol<IMethodSymbol>(method), false);

        // The two removed statements and the empty body of Local map back to the calls they replace
        var replacements = lowered.LoweredBody.DescendantNodes()
            .Where(static n => n is EmptyStatementSyntax ||
                               n is BlockSyntax { Statements.Count: 0, Parent: LocalFunctionStatementSyntax })
            .ToList();
        Assert.Equal(3, replacements.Count);
        Assert.All(
            replacements,
            replacement =>
            {
                var location = model.GetOriginalLocation(replacement);
                Assert.NotNull(location);
                Assert.Contains("Trace(", s_case.Value.Tree.GetText().ToString(location.SourceSpan),
                    StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Erase_BodyOfTheCompilation_RemovesEachForm()
    {
        var method = FindMethod("SplitArgumentsKernel");

        var erased = RemovedCallEraser.Erase(method.Body ?? throw new InvalidOperationException(), Model);

        var text = erased.NormalizeWhitespace().ToFullString();
        Assert.DoesNotContain("Trace", text, StringComparison.Ordinal);
        Assert.Contains("void Local(int value)", text, StringComparison.Ordinal);
        Assert.Contains("for (int k = 0; k < 2; k++)", text, StringComparison.Ordinal);
        Assert.Equal(2, erased.Statements.OfType<EmptyStatementSyntax>().Count());
    }
}
