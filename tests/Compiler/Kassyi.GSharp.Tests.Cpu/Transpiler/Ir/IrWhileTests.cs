#nullable enable
using System;
using System.Linq;
using System.Threading;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

[Category(TestCategory.Compiler)]
public class IrWhileTests
{
    private static Compilation Compilation => IrTestCases.Compile(IrTestCases.WhileCases);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_WhileLoop_IsAWhileNode()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "WhileWithBarrier");
        Assert.Equal(2, method.Body.Statements.Length);
        Assert.IsType<IrLocalDeclaration>(method.Body.Statements[0]);

        var @while = Assert.IsType<IrWhile>(method.Body.Statements[1]);
        var bodyBlock = Assert.IsType<IrBlock>(@while.Body);
        Assert.Equal(3, bodyBlock.Statements.Length);
        Assert.Empty(@while.Locals);
        Assert.Equal(IrUniformity.Unclassified, @while.Uniformity);

        var printed = IrTestCases.PrintLf(@while);
        Assert.StartsWith("while (i < n)\n{\n(ref data[i] = i)\n", printed);

        Assert.Empty(IrInvariants.Verify(method, Compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Build_OutVarInWhileCondition_IsALocalOfTheLoop()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "OutVarInCondition");
        var @while = Assert.IsType<IrWhile>(method.Body.Statements.Single(s => s is IrWhile));

        Assert.Single(@while.Locals);
        Assert.Equal("next", @while.Locals[0].Name);
        Assert.DoesNotContain(@while.Locals[0], method.Body.Locals);

        Assert.Empty(IrInvariants.Verify(method, Compilation));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Build_DoWhile_StaysOpaque()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "DoWhile");
        var opaque = Assert.IsType<IrOpaqueStatement>(method.Body.Statements.Single(s => s is IrOpaqueStatement));
        Assert.Equal(IrOpaqueReason.UnsupportedOperation, opaque.Reason);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("CheckedBlock")]
    [InlineData("UncheckedBlock")]
    public void Build_CheckedStatement_IsOpaque(string methodName)
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", methodName);
        var opaque = Assert.IsType<IrOpaqueStatement>(method.Body.Statements.Single(s => s is IrOpaqueStatement));
        Assert.Equal(IrOpaqueReason.UnsupportedShape, opaque.Reason);
        Assert.IsAssignableFrom<CheckedStatementSyntax>(opaque.Syntax);
        Assert.Equal(opaque.Syntax.Span, opaque.Source.Span);

        IrCoverage.AssertSpansAreNodes(method, Compilation);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Children_While_AreTheConditionThenTheBody()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "WhileWithBarrier");
        var @while = Assert.IsType<IrWhile>(method.Body.Statements.Single(s => s is IrWhile));

        var children = IrNodeChildren.Of(@while);
        Assert.Equal(2, children.Length);
        Assert.Same(@while.Condition, children[0]);
        Assert.Same(@while.Body, children[1]);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_WhileWithAnIntCondition_ReportsI26()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "WhileWithBarrier");
        var @while = Assert.IsType<IrWhile>(method.Body.Statements.Single(s => s is IrWhile));
        var nParam = method.Parameters.Single(p => p.Name == "n");

        var badCondition = new IrVariableReference(nParam, false, IrCategory.Value, default,
            @while.Condition.Source);
        var brokenWhile = @while.Update(badCondition, @while.Body);
        var statements = method.Body.Statements.SetItem(1, brokenWhile);
        var brokenMethod = new IrMethod(method.Symbol, method.Parameters, method.This,
            new IrBlock(statements, method.Body.Locals, method.Body.Source), method.Source);

        var violations = IrInvariants.Verify(brokenMethod, Compilation);
        var violation = Assert.Single(violations);
        Assert.StartsWith("I26", violation);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_ShuffleInWhileCondition_PlansTheConditionAndSkipsItsRewrite()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "ShuffleInCondition");
        var compilation = Compilation;
        var classified = IrClassifier.Classify(method, compilation);
        var plans = IrSpillPlanner.Plan(classified, compilation);

        var plan = Assert.Single(plans.Statements);
        var whileNode = Assert.IsType<IrWhile>(classified.Body.Statements.Single(s => s is IrWhile));
        Assert.Same(whileNode, plan.Statement);
        Assert.Equal(IrSpillFallback.None, plan.Fallback);

        var split = Assert.Single(plan.Steps.OfType<IrSpillSplit>());
        var binary = Assert.IsType<IrBinary>(whileNode.Condition);
        Assert.Same(binary.Left, split.Node);

        var rewriteResult = IrSpillRewriter.Apply(plan);
        Assert.Equal(IrSpillApplySkip.LoopCondition, rewriteResult.Skip);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_BarrierInWhileBody_PlansTheBodyStatement()
    {
        var method = IrTestCases.Build(IrTestCases.WhileCases, "IrWhileCases.Loops", "WhileWithBarrier");
        var compilation = Compilation;
        var classified = IrClassifier.Classify(method, compilation);
        var plans = IrSpillPlanner.Plan(classified, compilation);

        var plan = Assert.Single(plans.Statements);
        var whileNode = Assert.IsType<IrWhile>(classified.Body.Statements.Single(s => s is IrWhile));
        var bodyBlock = Assert.IsType<IrBlock>(whileNode.Body);
        var barrierStatement = bodyBlock.Statements[1];

        Assert.Same(barrierStatement, plan.Statement);
        Assert.Equal(IrSpillFallback.None, plan.Fallback);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HoistSplitPointsWithIr_ShuffleInWhileCondition_LeavesTheLoopToTheSyntaxPasses()
    {
        var compilation = Compilation;
        var symbol = IrTestCases.FindMethod(compilation, "IrWhileCases.Loops", "BarrierThenShuffleLoop");
        var declaration = (MethodDeclarationSyntax)symbol.DeclaringSyntaxReferences[0].GetSyntax();
        var body = (CSharpSyntaxNode?)declaration.Body ?? declaration.ExpressionBody!;
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);

        AstLoweringPipeline.HoistSplitPointsWithIr(body, !symbol.ReturnsVoid, model, symbol, out var outcomes);

        var whileSyntax = declaration.DescendantNodes().OfType<WhileStatementSyntax>().Single();
        var matching = outcomes.Where(o => o.Source == whileSyntax.Span).ToList();
        var outcome = Assert.Single(matching);

        Assert.False(outcome.IsLowered);
        Assert.Empty(outcome.Printed);
        Assert.Equal(IrSpillApplySkip.LoopCondition, outcome.Skip);
    }
}
