#nullable enable
using System;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.ZeroAlloc.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for the arguments of a suspending call or a warp shuffle that lowering evaluates into temporaries
/// (<see cref="WarpInstructionRewriter"/>): an identifier argument whose variable a later argument writes is evaluated
/// first as well, as C# reads it (#608), and the arguments of a shuffle are evaluated once, in the order written (#788).
/// </summary>
[Category(TestCategory.Compiler)]
public class SuspendingCallArgumentOrderTests
{
    private const string TestCase = "SuspendingArgumentOrder.cs";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Lower_IdentifierWrittenByLaterArgument_IsSpilledBeforeIt()
    {
        var lowered = LowerKernel("ReadThenWrite");

        var temps = GetArgumentTemps(lowered);
        Assert.Equal(2, temps.Length);
        Assert.Equal("a", GetTargetName(temps[0].Value));
        var inv = Assert.IsType<InvocationExpressionSyntax>(temps[1].Value);
        Assert.Equal("Next", Assert.IsType<IdentifierNameSyntax>(inv.Expression).Identifier.ValueText);
        var arg = Assert.Single(inv.ArgumentList.Arguments);
        Assert.True(arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword));
        Assert.Equal("a", GetTargetName(arg.Expression));

        Assert.Equal(temps.Select(static t => t.Name).ToArray(), GetResumedArguments(lowered));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Lower_IdentifierNotWrittenByLaterArgument_StaysInPlace()
    {
        var lowered = LowerKernel("ReadThenIndependentWrite");

        var temps = GetArgumentTemps(lowered);
        Assert.Single(temps);
        var inv = Assert.IsType<InvocationExpressionSyntax>(temps[0].Value);
        Assert.Equal("Next", Assert.IsType<IdentifierNameSyntax>(inv.Expression).Identifier.ValueText);
        var arg = Assert.Single(inv.ArgumentList.Arguments);
        Assert.True(arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword));
        Assert.Equal("b", GetTargetName(arg.Expression));

        Assert.Equal(["a", temps[0].Name], GetResumedArguments(lowered));
    }

    // Without a semantic model the later arguments are searched for a ref / out argument, an assignment or an increment
    // of the same name
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("S(a, Next(ref a))", true)]
    [InlineData("S(a, a++)", true)]
    [InlineData("S(a, a = 3)", true)]
    [InlineData("S(a, Next(ref b))", false)]
    [InlineData("S(a, a + 1)", false)]
    public void Rewrite_WithoutModel_SpillsIdentifierWrittenByName(string call, bool spillsFirstArgument)
    {
        var block = (BlockSyntax)SyntaxFactory.ParseStatement($"{{ x = {call}; }}");
        var invocation = block.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        block = block.ReplaceNode(invocation, invocation.WithAdditionalAnnotations(
            new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind,
                LoweringAnnotations.ValueReturningCallAnnotationData)));

        var rewritten = new WarpInstructionRewriter().Rewrite(block);

        var call0 = rewritten.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(static i => i.Expression is IdentifierNameSyntax { Identifier.ValueText: "S" });
        var first = call0.ArgumentList.Arguments[0].Expression;
        var spills = GetTargetName(first).StartsWith(GSharpInternalNames.Lowering.ArgumentTempPrefix,
            StringComparison.Ordinal);
        Assert.Equal(spillsFirstArgument, spills);
    }

    // A shuffle publishes its value before its state boundary and reads its arguments again after it, so its arguments
    // are evaluated once into temporaries, in the order written, though named out of parameter order (#788)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Rewrite_WarpShuffle_SpillsNamedArgumentsInTheOrderWritten()
    {
        var block = (BlockSyntax)SyntaxFactory.ParseStatement(
            "{ x = Warp.ShuffleSync(var: v++, width: W(), mask: M(), srcLane: L()); }");
        var invocation = block.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        block = block.ReplaceNode(invocation, invocation.WithAdditionalAnnotations(
            new SyntaxAnnotation(LoweringAnnotations.SyncIntrinsicAnnotationKind,
                LoweringAnnotations.ShuffleSyncAnnotationData)));

        var rewritten = new WarpInstructionRewriter().Rewrite(block);

        var temps = rewritten.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .ToArray()
            .AsStackEnumerable()
            .Select(static s => s.Declaration.Variables[0])
            .Where(static v => v.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.ArgumentTempPrefix,
                StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(4, temps.Length);
        var vInc = Assert.IsType<PostfixUnaryExpressionSyntax>(temps[0].Initializer?.Value);
        Assert.Equal(SyntaxKind.PostIncrementExpression, vInc.Kind());
        Assert.Equal("v", Assert.IsType<IdentifierNameSyntax>(vInc.Operand).Identifier.ValueText);
        Assert.Equal("W", Assert.IsType<IdentifierNameSyntax>(Assert.IsType<InvocationExpressionSyntax>(temps[1].Initializer?.Value).Expression).Identifier.ValueText);
        Assert.Equal("M", Assert.IsType<IdentifierNameSyntax>(Assert.IsType<InvocationExpressionSyntax>(temps[2].Initializer?.Value).Expression).Identifier.ValueText);
        Assert.Equal("L", Assert.IsType<IdentifierNameSyntax>(Assert.IsType<InvocationExpressionSyntax>(temps[3].Initializer?.Value).Expression).Identifier.ValueText);

        var shuffle = rewritten.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(static i => i.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Warp" },
                Name.Identifier.ValueText: "ShuffleSync"
            });
        Assert.Equal(
            temps.Select(static t => t.Identifier.ValueText).ToArray(),
            shuffle.ArgumentList.Arguments.Select(static a => Assert.IsType<IdentifierNameSyntax>(a.Expression).Identifier.ValueText).ToArray()
        );
    }

    // <frame>__gsharp_arg_temp_<n> = <value>; in statement order (the frame access stripped)
    private static (string Name, ExpressionSyntax Value)[] GetArgumentTemps(LoweredStateMachine lowered) =>
        lowered.LoweredBody.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .ToArray()
            .AsStackEnumerable()
            .Where(static a => GetTargetName(a.Left)
                .StartsWith(GSharpInternalNames.Lowering.ArgumentTempPrefix, StringComparison.Ordinal))
            .Select(static a => (GetTargetName(a.Left), a.Right))
            .ToArray();

    // The arguments of CombineAcrossBarrier inside __gsharp_resume(...)
    private static string[] GetResumedArguments(LoweredStateMachine lowered)
    {
        var call = lowered.LoweredBody.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(static i => i.Expression is IdentifierNameSyntax { Identifier.ValueText: "CombineAcrossBarrier" });
        return call.ArgumentList.Arguments
            .Select(static a => GetTargetName(a.Expression))
            .ToArray();
    }

    private static string GetTargetName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
        _ => string.Empty
    };

    private static LoweredStateMachine LowerKernel(string methodName)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == methodName);
        return AstLoweringPipeline.Lower(method, model, model.GetDeclaredSymbol<IMethodSymbol>(method),
            isDeviceFunction: false);
    }
}
