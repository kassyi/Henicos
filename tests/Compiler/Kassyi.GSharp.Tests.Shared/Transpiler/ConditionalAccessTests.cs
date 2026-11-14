#nullable enable
#pragma warning disable CS0649, CS0219, CS8600, CS8602, IDE0059, IDE0051
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The decision how a null-conditional access (<c>a?.b</c>) is written (<c>ConditionalAccess.Plan</c>), without running the
/// transpiler: what the receiver is, and whether the result is lifted to a nullable, passed on or not used.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ConditionalAccessTests
{
    // The only null-conditional access in the body of the named method of this file
    private static (ConditionalAccessExpressionSyntax Node, SemanticModel Model) FindAccess(
        string callerName,
        [CallerFilePath] string callerFilePath = "")
    {
        var (compilation, tree) = RoslynTestUtility.GetCallerSourceContext(callerFilePath);
        var model = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.ValueText == callerName)
            .DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>()
            .First();
        return (node, model);
    }

    [Theory]
    [InlineData(nameof(ConditionalAccessCases.ArrayLength), nameof(ConditionalReceiverKind.ArrayLike),
        nameof(ConditionalResultForm.Lifted), "Nullable")]
    [InlineData(nameof(ConditionalAccessCases.ArrayElement), nameof(ConditionalReceiverKind.ArrayLike),
        nameof(ConditionalResultForm.Lifted), "Nullable")]
    [InlineData(nameof(ConditionalAccessCases.NullableMember), nameof(ConditionalReceiverKind.Nullable),
        nameof(ConditionalResultForm.Lifted), "Nullable")]
    [InlineData(nameof(ConditionalAccessCases.ElementThatIsAnArray), nameof(ConditionalReceiverKind.ArrayLike),
        nameof(ConditionalResultForm.Passthrough), "")]
    [InlineData(nameof(ConditionalAccessCases.ElementThatIsNullable), nameof(ConditionalReceiverKind.ArrayLike),
        nameof(ConditionalResultForm.Passthrough), "Nullable")]
    [InlineData(nameof(ConditionalAccessCases.StackArray), nameof(ConditionalReceiverKind.NeverNull),
        nameof(ConditionalResultForm.Lifted), "Nullable")]
    [InlineData(nameof(ConditionalAccessCases.StringReceiver), nameof(ConditionalReceiverKind.Unsupported),
        nameof(ConditionalResultForm.Lifted), "Nullable")]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_ByReceiverAndAccess_ChoosesTheReceiverKindAndTheResultForm(
        string caller, string receiver, string form, string resultTypeName)
    {
        var (node, model) = FindAccess(caller);

        var plan = ConditionalAccess.Plan(node, model);

        // mutant: ConditionalAccess.Plan: choose Passthrough for a value type, or ArrayLike for a stack array
        Assert.Equal(receiver, plan.Receiver.ToString());
        Assert.Equal(form, plan.Form.ToString());
        Assert.Equal(resultTypeName, plan.ResultType?.Name);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_OfAnAccessUsedAsAStatement_IsAStatementWithoutAResultType()
    {
        var (node, model) = FindAccess(nameof(ConditionalAccessCases.AssignedElement));

        var plan = ConditionalAccess.Plan(node, model);

        // mutant: ConditionalAccess.Plan: drop the ExpressionStatementSyntax check
        Assert.Equal(ConditionalResultForm.Statement, plan.Form);
        Assert.Null(plan.ResultType);
    }
}

internal struct ConditionalCasePoint
{
    public int X;
}

internal static class ConditionalAccessCases
{
    public static void ArrayLength(int[]? a)
    {
        int? n = a?.Length;
    }

    public static void ArrayElement(int[]? a)
    {
        int? n = a?[1];
    }

    public static void NullableMember(ConditionalCasePoint? p)
    {
        int? n = p?.X;
    }

    public static void ElementThatIsAnArray(int[][]? jagged)
    {
        int[]? n = jagged?[0];
    }

    public static void ElementThatIsNullable(int?[]? a)
    {
        int? n = a?[0];
    }

    public static void StackArray()
    {
        int[] local = new int[4];
        int? n = local?.Length;
    }

    public static void StringReceiver(string? s)
    {
        int? n = s?.Length;
    }

    public static void AssignedElement(int[]? a)
    {
        a?[0] = 1;
    }
}
