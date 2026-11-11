#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the statements of the IR builder (#1561), the identity of variables, and the positions of nodes: one
/// declaration per local, expression bodies, <see langword="if"/>, opaque loops, one instance per variable, and spans
/// in the trees of the compilation.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrBuilderStatementTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SeveralDeclarators_AreOneDeclarationEachInOrder()
    {
        var method = IrTestCases.BuildCase("MultipleDeclarators");

        Assert.Equal("method Ir.MultipleDeclarators()\n{\nlocal int a = 1\nlocal int b = a\n}\n",
            IrPrinter.Print(method).Replace("\r", ""));
        var second = Assert.IsType<IrLocalDeclaration>(method.Body.Statements[1]);
        var read = Assert.IsType<IrVariableReference>(second.Initializer);
        Assert.Same(Assert.IsType<IrLocalDeclaration>(method.Body.Statements[0]).Variable, read.Variable);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ExpressionBody_IsOneReturn() =>
        Assert.Equal("method Ir.ExpressionBody(int x)\n{\nreturn (x + 1)\n}\n",
            IrPrinter.Print(IrTestCases.BuildCase("ExpressionBody")).Replace("\r", ""));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void If_KeepsItsBranchesAsWritten()
    {
        var @if = Assert.IsType<IrIf>(IrTestCases.Statement(IrTestCases.BuildCase("IfElse")));

        Assert.IsType<IrExpressionStatement>(@if.Then);
        Assert.IsType<IrBlock>(@if.Else);
        Assert.Equal("if (x > 0)\n(ref data[0] = 1)\nelse\n{\n(ref data[0] = 2)\n}",
            IrPrinter.Print(@if).Replace("\r", ""));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Loop_IsAnOpaqueStatement()
    {
        var statement = IrTestCases.Statement(IrTestCases.BuildCase("Loop"));

        var opaque = Assert.IsType<IrOpaqueStatement>(statement);
        Assert.Equal(IrOpaqueReason.UnsupportedOperation, opaque.Reason);
        Assert.True(IrInvariants.ContainsOpaque(statement));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SameLocal_IsOneInstance()
    {
        var method = IrTestCases.BuildCase("SameLocalTwice");
        var declared = Assert.IsType<IrLocalDeclaration>(method.Body.Statements[0]).Variable;
        var assignment = Assert.IsType<IrAssignment>(
            Assert.IsType<IrExpressionStatement>(method.Body.Statements[1]).Expression);
        var sum = Assert.IsType<IrBinary>(assignment.Value);

        Assert.Same(declared, Assert.IsType<IrVariableReference>(sum.Left).Variable);
        Assert.Same(declared, Assert.IsType<IrVariableReference>(sum.Right).Variable);
        Assert.NotSame(sum.Left, sum.Right);
        Assert.Contains(declared, method.Body.Locals);
        Assert.Same(method.Parameters[0],
            Assert.IsType<IrVariableReference>(Assert.IsType<IrElementReference>(assignment.Target).Receiver).Variable);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void OutVarLocal_IsInTheBlockLocals()
    {
        var method = IrTestCases.BuildCase("OutVar");

        Assert.Contains(method.Body.Locals, static local => local.Name == "declared");
        Assert.Empty(IrInvariants.Verify(method, IrTestCases.Compile(IrTestCases.BuilderCases)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Source_PointsIntoTheTreeOfTheMethodWhenTheCompilationHasSeveral()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase(IrTestCases.OrderCases),
            RoslynTestUtility.ReadTestCase(IrTestCases.BuilderCases));
        var symbol = IrTestCases.FindMethod(compilation, "IrCases.Ir", "Defaults");
        var method = IrBuilder.TryBuild(symbol, compilation, CancellationToken.None);

        Assert.NotNull(method);
        var tree = symbol.DeclaringSyntaxReferences[0].SyntaxTree;
        Assert.Equal(compilation.SyntaxTrees.ToList().IndexOf(tree), method.Source.TreeOrdinal);
        Assert.NotEqual(0, method.Source.TreeOrdinal);
        var call = Assert.IsType<IrCall>(IrTestCases.Result(method));
        var location = call.Source.ToLocation(compilation);
        Assert.Same(tree, location.SourceTree);
        Assert.Equal("Offset(a)", location.SourceTree?.GetText().ToString(location.SourceSpan));
        Assert.Empty(IrInvariants.Verify(method, compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ImplicitNodes_AreSynthesizedAndExplicitOnesAreNot()
    {
        var conversion = Assert.IsType<IrConversion>(IrTestCases.Result(IrTestCases.BuildCase("ImplicitNumeric")));
        var cast = Assert.IsType<IrConversion>(IrTestCases.Result(IrTestCases.BuildCase("ExplicitNumeric")));

        Assert.True(conversion.Source.Synthesized);
        Assert.Equal(conversion.Operand.Source.Span, conversion.Source.Span);
        Assert.False(cast.Source.Synthesized);
        Assert.False(cast.Operand.Source.Synthesized);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryCase_KeepsTheInvariants()
    {
        var compilation = IrTestCases.Compile(IrTestCases.BuilderCases);
        var type = compilation.GetTypeByMetadataName("IrCases.Ir") ?? throw new InvalidOperationException();
        foreach (var symbol in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (IrBuilder.TryBuild(symbol, compilation, CancellationToken.None) is not { } method)
            {
                continue;
            }

            Assert.Equal([], IrInvariants.Verify(method, compilation).Select(v => symbol.Name + ": " + v));
            IrCoverage.AssertSpansAreNodes(method, compilation);
        }
    }
}
