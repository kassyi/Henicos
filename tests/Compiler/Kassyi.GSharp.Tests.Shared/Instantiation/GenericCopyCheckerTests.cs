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
        var compilation = CreateCompilation();
        var decimalType = compilation.GetSpecialType(SpecialType.System_Decimal);
        var instance = Instance(compilation, "Identities", decimalType);
        var plan = GenericCopies.Plan(compilation, instance);
        var rewrites = plan.Rewrites;
        foreach (var entry in plan.Rewrites)
        {
            if (entry.Value is CopyRewrite.IdentityConstant { Value: 0 } identity)
            {
                rewrites = rewrites.SetItem(entry.Key, identity with { Value = 1 });
            }
        }

        var wrongSet = GenericCopies.Create(compilation, [plan with { Rewrites = rewrites }]);
        var copyDeclaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(wrongSet.Copies.Single().Declaration);
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(
            instance.Definition.DeclaringSyntaxReferences.Single().GetSyntax());
        var problems = GenericCopyChecker.Check(
            CopyNodeIndex.List(declaration),
            declaration,
            compilation.GetSemanticModel(declaration.SyntaxTree),
            copyDeclaration,
            wrongSet.Compilation.GetSemanticModel(copyDeclaration.SyntaxTree),
            plan,
            new InstanceSymbolMap(instance, compilation));

        var identityProblems = problems
            .Where(p => p.Reason.Contains("identity", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, identityProblems.Length);
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
        var compilation = CreateCompilation();
        var decimalType = compilation.GetSpecialType(SpecialType.System_Decimal);
        var instance = Instance(compilation, "Identities", decimalType);
        var plan = GenericCopies.Plan(compilation, instance);
        var set = GenericCopies.Create(compilation, [plan]);
        var copyDeclaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(set.Copies.Single().Declaration);
        var zeroTokens = copyDeclaration.DescendantTokens()
            .Where(t => t.IsKind(SyntaxKind.NumericLiteralToken) && t.Value is 0)
            .ToArray();
        var scaledDeclaration = copyDeclaration.ReplaceTokens(
            zeroTokens,
            (_, _) => SyntaxFactory.Literal("0.0m", 0.0m));
        var oldTree = copyDeclaration.SyntaxTree;
        var newRoot = oldTree.GetRoot().ReplaceNode(copyDeclaration, scaledDeclaration);
        var newTree = oldTree.WithRootAndOptions(newRoot, oldTree.Options);
        var updatedCompilation = set.Compilation.ReplaceSyntaxTree(oldTree, newTree);
        var updatedCopy = newTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == copyDeclaration.Identifier.ValueText);
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(
            instance.Definition.DeclaringSyntaxReferences.Single().GetSyntax());

        var problems = GenericCopyChecker.Check(
            CopyNodeIndex.List(declaration),
            declaration,
            compilation.GetSemanticModel(declaration.SyntaxTree),
            updatedCopy,
            updatedCompilation.GetSemanticModel(newTree),
            plan,
            new InstanceSymbolMap(instance, compilation));

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

        Assert.NotEmpty(problems);
        Assert.DoesNotContain(problems, p => p.Reason.Contains("lost", StringComparison.Ordinal));
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

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Negate", "System.UInt32", "negate in the type")]
    [InlineData("Negate", "System.SByte", "narrow the negation")]
    public void Check_WrongNegationConstantWithPlanPresent_RejectsTheCopy(
        string method,
        string typeArgument,
        string expectedReason)
    {
        var problems = CheckMutatedCopy(
            method,
            typeArgument,
            copy =>
            {
                var zero = copy.DescendantTokens()
                    .Single(t => t.IsKind(SyntaxKind.NumericLiteralToken) && t.Value is 0);
                return copy.ReplaceToken(zero, SyntaxFactory.Literal(1));
            });

        Assert.NotEmpty(problems);
        Assert.DoesNotContain(problems, p => p.Reason.Contains("lost", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Reason.Contains("does not compile", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Reason.Contains(expectedReason, StringComparison.Ordinal));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("MaxValue", "System.Int32", "MaxValue", "MinValue")]
    [InlineData("Max", "System.Char", "Max", "Min")]
    public void Check_WrongStaticMemberOrMathCallWithPlanPresent_RejectsTheCopy(
        string method,
        string typeArgument,
        string fromName,
        string toName)
    {
        var problems = CheckMutatedCopy(
            method,
            typeArgument,
            copy => copy.ReplaceTokens(
                copy.DescendantTokens()
                    .Where(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == fromName)
                    .ToArray(),
                (_, _) => SyntaxFactory.Identifier(toName)));

        Assert.NotEmpty(problems);
    }
}
