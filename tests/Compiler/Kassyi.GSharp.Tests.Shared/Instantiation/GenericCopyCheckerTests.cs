#nullable enable

using Kassyi.GSharp.Compiler.Instantiation;
using Microsoft.CodeAnalysis.Text;
using static Kassyi.GSharp.Tests.Shared.Instantiation.GenericCopyTestSupport;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>
/// Verifies the checker of the closed copies: a copy that differs from its declaration anywhere, by a rewrite the plan
/// lacks or by a wrong form of one it has, is rejected with the reason and the node of the difference.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericCopyCheckerTests
{
    // The probe that motivated the pinning: Helper(s) with T = int binds Helper(int) once the copy says int
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_InferredGenericCallWithoutItsPin_RejectsTheCopy()
    {
        var copy = CreateWithout<CopyRewrite.ExplicitTypeArguments>("CallsHelper", "System.Int32");

        Assert.False(copy.IsFaithful);
        var finding = Assert.Single(copy.Findings, f => f.Reason.Contains("binds", StringComparison.Ordinal));
        Assert.Equal(LineOf("public static int CallsHelper<T>"), finding.Location.GetLineSpan().StartLinePosition.Line);
    }

    // A call that the declaration binds to a candidate that is not generic, where the copy binds another one
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_OverloadThatTheClosedTypeBindsAnother_RejectsTheCopy()
    {
        var (copy, _) = CreateCopy("CallsOver", "System.Int32");

        Assert.False(copy.IsFaithful);
        Assert.Contains(copy.Findings, f => f.Reason.Contains("Over", StringComparison.Ordinal));
    }

    // A copy that does not compile is not faithful: ?. on an unconstrained type parameter has no form for int
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_CopyThatDoesNotCompile_IsNotFaithful()
    {
        var (copy, _) = CreateCopy("NullConditional", "System.Int32");

        Assert.False(copy.IsFaithful);
        Assert.Contains(copy.Findings, f => f.Reason.Contains("does not compile", StringComparison.Ordinal));
    }

    // A closed form that the plan wrote and the copy loses leaves a call the instance does not have: the check sees it
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_ConstrainedCallWithoutItsHelper_RejectsTheCopy()
    {
        var copy = CreateWithout<CopyRewrite.ConstrainedCall>("CallFoo", "App.Both");

        Assert.False(copy.IsFaithful);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_StaticMemberWithoutItsClosedForm_RejectsTheCopy()
    {
        var copy = CreateWithout<CopyRewrite.StaticMember>("MaxValue", "System.Int32");

        Assert.False(copy.IsFaithful);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_MathCallWithoutItsClosedForm_RejectsTheCopy()
    {
        var copy = CreateWithout<CopyRewrite.MathCall>("Max", "System.Char");

        Assert.False(copy.IsFaithful);
    }

    // The second probe: d[0].Ext() with T = Odd binds Odd.Ext() once the copy says Odd
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_ExtensionCallWithoutItsPin_RejectsTheCopy()
    {
        var copy = CreateWithout<CopyRewrite.StaticExtensionCall>("CallsExt", "App.Odd");

        Assert.False(copy.IsFaithful);
        Assert.Contains(copy.Findings, f => f.Reason.Contains("Odd.Ext", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_IdentityConstantWithWrongValue_RejectsTheCopy()
    {
        var problems = CheckMutatedCopy(
            "Identities",
            "System.Decimal",
            copy => copy,
            plan => plan with
            {
                Rewrites = plan.Rewrites.ToImmutableDictionary(
                    entry => entry.Key,
                    entry => entry.Value is CopyRewrite.IdentityConstant { Value: 0 } identity
                        ? identity with { Value = 1 }
                        : entry.Value),
            });

        Assert.Equal(2, problems.Count(p => p.Reason.Contains("identity", StringComparison.Ordinal)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_CompoundDivisionWithoutItsNarrowing_RejectsTheCopy()
    {
        var copy = CreateWithout<CopyRewrite.NarrowedCompoundDivision>("CheckedCompoundDivide", "System.SByte");

        Assert.False(copy.IsFaithful);
        Assert.DoesNotContain(copy.Findings, f => f.Reason.Contains("does not compile", StringComparison.Ordinal));
        Assert.Contains(copy.Findings, f => f.Reason.Contains("narrow", StringComparison.Ordinal));
        Assert.Contains(copy.Findings, f => f.Reason.Contains("converts to", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Check_DecimalIdentityWithDifferentScale_AcceptsTheCopy()
    {
        var problems = CheckMutatedCopy(
            "Identities",
            "System.Decimal",
            copy => copy.ReplaceTokens(
                copy.DescendantTokens().Where(t => t.IsKind(SyntaxKind.NumericLiteralToken) && t.Value is 0).ToArray(),
                (_, _) => SyntaxFactory.Literal("0.0m", 0.0m)));

        Assert.Empty(problems);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_ConstrainedCallWithMoreSpecificOverload_RejectsTheCopy()
    {
        var (copy, _) = CreateCopy("CallOverloaded", "App.OverloadedTarget");

        Assert.False(copy.IsFaithful);
        Assert.Contains(copy.Findings, f => f.Reason.Contains("binds", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_StringAdditionWithTypeParameter_RejectsTheCopy()
    {
        var (copy, _) = CreateCopy("ConcatWith", "System.String");

        Assert.False(copy.IsFaithful);
        Assert.Contains(copy.Findings, f => f.Reason.Contains("binds", StringComparison.Ordinal));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("CallStaticGen", "App.StaticGenExplicit")]
    [InlineData("CallInstGen", "App.InstGenExplicit")]
    public void Check_ExplicitGenericInterfaceMethod_RejectsTheCopy(string method, string typeArgument)
    {
        var (copy, _) = CreateCopy(method, typeArgument);

        Assert.False(copy.IsFaithful);
        Assert.DoesNotContain(copy.Findings, f => f.Reason.Contains("does not compile", StringComparison.Ordinal));
        Assert.Contains(copy.Findings, f => f.Reason.Contains("explicitly", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_WrongNarrowedOperatorWithPlanPresent_RejectsTheCopy()
    {
        var problems = CheckMutatedCopy(
            "Sum",
            "System.Byte",
            copy => copy.ReplaceNodes(
                copy.DescendantNodes().OfType<BinaryExpressionSyntax>()
                    .Where(b => b.IsKind(SyntaxKind.AddExpression))
                    .Take(1),
                (binary, _) => SyntaxFactory.BinaryExpression(
                    SyntaxKind.SubtractExpression, binary.Left, binary.Right)));

        var problem = Assert.Single(problems);
        Assert.Contains("does not narrow the operator", problem.Reason, StringComparison.Ordinal);
        Assert.Equal(RewriteIndex<CopyRewrite.NarrowedOperator>("Sum", "System.Byte"), problem.NodeIndex);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("CheckedCompoundDivide", "System.SByte", SyntaxKind.DivideAssignmentExpression, "b")]
    [InlineData("ShiftAssign", "System.Byte", SyntaxKind.LeftShiftAssignmentExpression, "n")]
    public void Check_UnnarrowedCompoundRewriteWithPlanPresent_RejectsTheCopy(
        string method,
        string typeArgument,
        SyntaxKind assignmentKind,
        string rightName)
    {
        var problems = CheckMutatedCopy(
            method,
            typeArgument,
            copy => copy.ReplaceNodes(
                copy.DescendantNodes().OfType<AssignmentExpressionSyntax>().Take(1),
                (original, _) =>
                {
                    var right = original.Right.DescendantNodes().OfType<IdentifierNameSyntax>()
                        .Single(id => id.Identifier.ValueText == rightName);
                    return SyntaxFactory.AssignmentExpression(assignmentKind, original.Left, right)
                        .WithAdditionalAnnotations(original.GetAnnotations(CopyNodeIndex.AnnotationKind));
                }));

        Assert.NotEmpty(problems);
        Assert.DoesNotContain(problems, p => p.Reason.Contains("lost", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_WrongShiftMaskWithPlanPresent_RejectsTheCopy()
    {
        var problems = CheckMutatedCopy(
            "Shift",
            "System.Byte",
            copy =>
            {
                var seven = copy.DescendantTokens()
                    .Single(t => t.IsKind(SyntaxKind.NumericLiteralToken) && t.Value is 7);
                return copy.ReplaceToken(seven, SyntaxFactory.Literal(31));
            });

        Assert.NotEmpty(problems);
        Assert.DoesNotContain(problems, p => p.Reason.Contains("lost", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Reason.Contains("does not compile", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Reason.Contains("narrow the shift", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_WrongStaticMemberWithPlanPresent_RejectsTheCopy()
    {
        var problems = CheckRenamedIdentifier("MaxValue", "System.Int32", "MaxValue", "MinValue");

        var problem = Assert.Single(problems);
        Assert.Contains("does not read 'Int32.MaxValue'", problem.Reason, StringComparison.Ordinal);
        Assert.Equal(RewriteIndex<CopyRewrite.StaticMember>("MaxValue", "System.Int32"), problem.NodeIndex);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_WrongMathCallWithPlanPresent_RejectsTheCopy()
    {
        var problems = CheckRenamedIdentifier("Max", "System.Char", "Max", "Min");

        var problem = Assert.Single(problems);
        Assert.Contains("does not call 'System.Math.Max'", problem.Reason, StringComparison.Ordinal);
        Assert.Equal(RewriteIndex<CopyRewrite.MathCall>("Max", "System.Char"), problem.NodeIndex);
    }

    // -0.0 == 0 in C#, but T.Zero is +0.0 and the two differ for 1 / x and Math.CopySign
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Identities", "System.Double")]
    [InlineData("Identities", "System.Single")]
    [InlineData("OneOverZero", "System.Double")]
    public void Check_NegativeZeroForZeroIdentity_RejectsTheCopy(string method, string typeArgument)
    {
        var problems = CheckMutatedCopy(
            method,
            typeArgument,
            copy => copy.ReplaceNodes(
                copy.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(l => l.Token.Value is 0).Take(1),
                (literal, _) => SyntaxFactory.PrefixUnaryExpression(
                        SyntaxKind.UnaryMinusExpression,
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0.0)))
                    .WithAdditionalAnnotations(literal.GetAnnotations(CopyNodeIndex.AnnotationKind))));

        var problem = Assert.Single(problems);
        Assert.Contains("does not give the identity of the type", problem.Reason, StringComparison.Ordinal);
    }

    private static ImmutableArray<CopyProblem> CheckRenamedIdentifier(
        string method,
        string typeArgument,
        string fromName,
        string toName) =>
        CheckMutatedCopy(
            method,
            typeArgument,
            copy => copy.ReplaceTokens(
                copy.DescendantTokens()
                    .Where(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == fromName)
                    .ToArray(),
                (_, _) => SyntaxFactory.Identifier(toName)));

    // The node number of the occurrence-th rewrite of the kind in the plan of the instance
    private static int RewriteIndex<TRewrite>(string method, string typeArgument, int occurrence = 0)
        where TRewrite : CopyRewrite
    {
        var compilation = CreateCompilation();
        var type = compilation.GetTypeByMetadataName(typeArgument) ??
                   throw new InvalidOperationException($"No type '{typeArgument}'.");
        var plan = GenericCopies.Plan(compilation, Instance(compilation, method, type));
        return plan.Rewrites.Where(e => e.Value is TRewrite).Select(e => e.Key).Order().ElementAt(occurrence);
    }
}
