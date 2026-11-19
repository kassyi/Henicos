#nullable enable
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The decision how a null-conditional access (<c>a?.b</c>) is written (<c>ConditionalAccess.Plan</c>), without
/// running the transpiler: what the receiver is, whether the result is lifted to a nullable, passed on or not used,
/// and the GSH0003 text of a receiver that cannot be written.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ConditionalAccessTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/ConditionalAccessCases.cs"));

    // The first (outermost) null-conditional access in the body of the named method of the test case
    private static (ConditionalAccessExpressionSyntax Node, SemanticModel Model) FindAccess(string caseName)
    {
        var tree = s_compilation.SyntaxTrees.Single();
        var node = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == caseName)
            .DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>()
            .First();
        return (node, s_compilation.GetSemanticModel(tree));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ConditionalAccessCases_Compiled_HaveNoErrors() =>
        Assert.DoesNotContain(
            s_compilation.GetDiagnostics(),
            static d => d.Severity == DiagnosticSeverity.Error);

    [Theory]
    [InlineData("ArrayLength", nameof(ConditionalReceiverKind.ArrayLike),
        nameof(ConditionalResultForm.Lifted), "Nullable", null)]
    [InlineData("ArrayElement", nameof(ConditionalReceiverKind.ArrayLike),
        nameof(ConditionalResultForm.Lifted), "Nullable", null)]
    [InlineData("NullableMember", nameof(ConditionalReceiverKind.Nullable),
        nameof(ConditionalResultForm.Lifted), "Nullable", null)]
    [InlineData("ElementThatIsAnArray", nameof(ConditionalReceiverKind.ArrayLike),
        nameof(ConditionalResultForm.Passthrough), "", null)]
    [InlineData("ElementThatIsNullable", nameof(ConditionalReceiverKind.ArrayLike),
        nameof(ConditionalResultForm.Passthrough), "Nullable", null)]
    [InlineData("StackArray", nameof(ConditionalReceiverKind.NeverNull),
        nameof(ConditionalResultForm.Lifted), "Nullable", null)]
    [InlineData("StringReceiver", nameof(ConditionalReceiverKind.Unsupported),
        nameof(ConditionalResultForm.Lifted), "Nullable", ConditionalAccess.UnsupportedReceiver)]
    [InlineData("NullableIndexer", nameof(ConditionalReceiverKind.Unsupported),
        nameof(ConditionalResultForm.Lifted), "Nullable", ConditionalAccess.NullableElementAccess)]
    [InlineData("NullableSegmentMember", nameof(ConditionalReceiverKind.Unsupported),
        nameof(ConditionalResultForm.Lifted), "Nullable", ConditionalAccess.NullableArrayView)]
    [InlineData("NullableSegmentElement", nameof(ConditionalReceiverKind.Unsupported),
        nameof(ConditionalResultForm.Lifted), "Nullable", ConditionalAccess.NullableArrayView)]
    [InlineData("NullableVectorMember", nameof(ConditionalReceiverKind.Unsupported),
        nameof(ConditionalResultForm.Lifted), "Nullable", ConditionalAccess.NullableVector)]
    [InlineData("NullableHolderWithNestedElement", nameof(ConditionalReceiverKind.Nullable),
        nameof(ConditionalResultForm.Passthrough), "Nullable", null)]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_ByReceiverAndAccess_ChoosesTheReceiverKindTheResultFormAndTheProblem(
        string caseName, string receiver, string form, string resultTypeName, string? problem)
    {
        var (node, model) = FindAccess(caseName);

        var plan = ConditionalAccess.Plan(node, model);

        // mutant: ConditionalAccess.Plan: choose Passthrough for a value type, ArrayLike for a stack array, Nullable
        // for an indexer, an array view or a vector of a Nullable<T>, or read the last binding instead of the first
        Assert.Equal(receiver, plan.Receiver.ToString());
        Assert.Equal(form, plan.Form.ToString());
        Assert.Equal(resultTypeName, plan.ResultType?.Name);
        Assert.Equal(problem, plan.Problem);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_OfAnAccessUsedAsAStatement_IsAStatementWithoutAResultType()
    {
        var (node, model) = FindAccess("AssignedElement");

        var plan = ConditionalAccess.Plan(node, model);

        // mutant: ConditionalAccess.Plan: drop the ExpressionStatementSyntax check
        Assert.Equal(ConditionalResultForm.Statement, plan.Form);
        Assert.Null(plan.ResultType);
        Assert.Null(plan.Problem);
    }
}
