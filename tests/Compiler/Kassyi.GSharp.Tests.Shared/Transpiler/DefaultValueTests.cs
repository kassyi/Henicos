#nullable enable
#pragma warning disable CS0649, CS0219, CS8600, IDE0059
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Literals;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The decision how <c>default(T)</c> and the <c>default</c> literal are written (<c>DefaultValue.Plan</c>), without
/// running the transpiler: <c>{}</c> where a braced list may stand, the typed value everywhere else.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class DefaultValueTests
{
    // The only default in the body of the named method of this file
    private static (ExpressionSyntax Node, SemanticModel Model) FindDefault(
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
            .OfType<ExpressionSyntax>()
            .Single(e => e.IsKind(SyntaxKind.DefaultLiteralExpression) || e.IsKind(SyntaxKind.DefaultExpression));
        return (node, model);
    }

    [Theory]
    [InlineData(nameof(DefaultPositionCases.TypedLocalInitializer))]
    [InlineData(nameof(DefaultPositionCases.SimpleAssignmentRight))]
    [InlineData(nameof(DefaultPositionCases.TypedLocalInitializerOfDefaultExpression))]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_WhereABracedListMayStand_IsBraced(string caller)
    {
        var (node, model) = FindDefault(caller);

        var plan = DefaultValue.Plan(node, model);

        // mutant: IsBracedListPosition: return false for an EqualsValueClause or a simple assignment
        Assert.Equal(DefaultValueForm.Braced, plan.Form);
        Assert.Null(plan.Type);
    }

    [Theory]
    [InlineData(nameof(DefaultPositionCases.VarInitializer), "Int32")]
    [InlineData(nameof(DefaultPositionCases.CompoundAssignmentRight), "Int64")]
    [InlineData(nameof(DefaultPositionCases.ParenthesizedAssignmentRight), "Int64")]
    [InlineData(nameof(DefaultPositionCases.ConditionalArm), "UInt32")]
    [InlineData(nameof(DefaultPositionCases.ComparisonOperand), "Byte")]
    [InlineData(nameof(DefaultPositionCases.ReturnValue), "Int64")]
    [InlineData(nameof(DefaultPositionCases.ArgumentOfACall), "UInt64")]
    [InlineData(nameof(DefaultPositionCases.MemberAccessTarget), "DefaultPositionPair")]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_WhereNoBracedListMayStand_IsTheTypedValueOfTheConvertedType(string caller, string type)
    {
        var (node, model) = FindDefault(caller);

        var plan = DefaultValue.Plan(node, model);

        // mutant: IsBracedListPosition: return true for a compound assignment, a return, or a parenthesized right side
        Assert.Equal(DefaultValueForm.Typed, plan.Form);
        Assert.Equal(type, plan.Type?.Name);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_WhenTheTypeDidNotResolveOutsideABracedPosition_ThrowsAnInternalError()
    {
        // A default literal that the semantic model does not know, in a position that needs a type
        var unbound = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
        var comparison = SyntaxFactory.BinaryExpression(
            SyntaxKind.EqualsExpression,
            SyntaxFactory.IdentifierName("x"),
            unbound);
        var (_, model) = FindDefault(nameof(DefaultPositionCases.VarInitializer));

        // mutant: DefaultValue.Plan: return a braced plan instead of throwing
        Assert.Throws<InvalidOperationException>(() => DefaultValue.Plan(comparison.Right, model));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForAnArgumentWithoutAType_IsBraced()
    {
        var unbound = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
        var argument = SyntaxFactory.Argument(unbound);
        var (_, model) = FindDefault(nameof(DefaultPositionCases.VarInitializer));

        var plan = DefaultValue.Plan(argument.Expression, model);

        // mutant: DefaultValue.Plan: drop the ArgumentSyntax check
        Assert.Equal(DefaultValueForm.Braced, plan.Form);
    }
}

internal struct DefaultPositionPair
{
    public int X;
}

internal static class DefaultPositionCases
{
    private static long Take(ulong value) => (long)value;

    public static void TypedLocalInitializer()
    {
        int x = default;
    }

    public static void TypedLocalInitializerOfDefaultExpression()
    {
        DefaultPositionPair p = default(DefaultPositionPair);
    }

    public static void SimpleAssignmentRight()
    {
        int x;
        x = default;
    }

    public static void VarInitializer()
    {
        var z = default(int);
    }

    public static void CompoundAssignmentRight()
    {
        long x = 1;
        x += default(long);
    }

    public static void ParenthesizedAssignmentRight()
    {
        long x;
        x = (default(long));
    }

    public static void ConditionalArm(bool c)
    {
        uint u = c ? default : 5u;
    }

    public static void ComparisonOperand(byte b)
    {
        bool e = b == default(byte);
    }

    public static long ReturnValue()
    {
        return default;
    }

    public static long ArgumentOfACall()
    {
        return Take(default);
    }

    public static int MemberAccessTarget()
    {
        return default(DefaultPositionPair).X;
    }
}
