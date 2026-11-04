namespace Kassyi.GSharp.Tests.Shared.Transpiler;

using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class StatementPreludeTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/StatementPreludeCases.cs"));

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Emit_DeclarationExpressionWithoutTypeSymbol_ThrowsInvalidOperationException()
    {
        // Synthesize a detached DeclarationExpressionSyntax ("out var x") not bound in the SemanticModel's tree.
        // When lowered trees lose original node annotations, GetTypeSymbol returns null.
        // StatementPrelude.Emit must throw an InvalidOperationException (which GpuKernelGenerator translates to GSH0048)
        // rather than silently emitting "void x;" or "auto x;".
        var tree = s_compilation.SyntaxTrees.Single();
        var model = s_compilation.GetSemanticModel(tree);

        using var visitor = RoslynToCudaVisitor.Rent(new StandardCppDialect(), model);
        using var writer = new SourceWriter();
        visitor.Context.Emitter.Writer = writer;

        var declExpr = SyntaxFactory.DeclarationExpression(
            SyntaxFactory.IdentifierName("var").WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier("x")));
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName("Foo"),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(null, SyntaxFactory.Token(SyntaxKind.OutKeyword).WithTrailingTrivia(SyntaxFactory.Space), declExpr))));
        var statement = SyntaxFactory.ExpressionStatement(invocation);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StatementPrelude.Emit(statement, ref visitor.Context));

        Assert.Contains("'var x'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Emit_DeclarationExpressionWithTypeSymbol_EmitsTypeAndName()
    {
        var tree = s_compilation.SyntaxTrees.Single();
        var model = s_compilation.GetSemanticModel(tree);

        var statement = tree.GetRoot().DescendantNodes().OfType<ExpressionStatementSyntax>()
            .First(s => s.ToString().Contains("TryGet", StringComparison.Ordinal));

        using var visitor = RoslynToCudaVisitor.Rent(new StandardCppDialect(), model);
        using var writer = new SourceWriter();
        visitor.Context.Emitter.Writer = writer;

        StatementPrelude.Emit(statement, ref visitor.Context);

        var output = writer.ToString();
        Assert.Contains("int", output, StringComparison.Ordinal);
        Assert.Contains("x;", output, StringComparison.Ordinal);
    }
}
