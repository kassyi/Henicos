#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler.Managed;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the split pass of the IR as the first pass of the lowering (#1561): the syntax it writes for each form, the
/// statements it leaves to the syntax passes and why, the names it shares with them and the frame of the state
/// machine it leads to.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrSplitLoweringTests
{
    private const string Cases = "Ir/IrSplitLoweringCases.cs";
    private const string PlannerCases = "Ir/IrSpillPlannerCases.cs";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task HoistSplitPointsWithIr_EveryCase_WritesTheStatementsOfTheSnapshot()
    {
        var sections = new List<string>();
        foreach (var (declaration, model) in Declarations(Cases).Concat(Declarations(PlannerCases)))
        {
            var tree = declaration.SyntaxTree;
            var symbol = model.GetDeclaredSymbol(declaration) ??
                         throw new InvalidOperationException("A method of the cases has no symbol.");
            var body = (CSharpSyntaxNode?)declaration.Body ?? declaration.ExpressionBody;
            if (body is null)
            {
                continue;
            }

            var hoisted = AstLoweringPipeline.HoistSplitPointsWithIr(body, !symbol.ReturnsVoid, model, symbol,
                out var outcomes);
            if (outcomes.Count == 0)
            {
                continue;
            }

            var text = tree.GetText();
            var header = string.Join("\n", outcomes.Select(outcome =>
                $"    // {IrSpillTrace.Squeeze(text.ToString(outcome.Source))} -> {Describe(outcome)}"));
#pragma warning disable RS0030 // The .verified.cs snapshot is the text of the lowered tree
            var formatted = hoisted.NormalizeWhitespace().ToFullString();
#pragma warning restore RS0030
            sections.Add($"{header}\n    public void {symbol.Name}()\n{formatted}");
        }

        var snapshot = "public class IrSplitLowering_Snapshot\n{\n" + string.Join("\n\n", sections) + "\n}\n";
        return Verifier.Verify(snapshot, "cs")
            .UseFastHashSnapshotName(nameof(HoistSplitPointsWithIr_EveryCase_WritesTheStatementsOfTheSnapshot));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Rewrite_CallTheSyntaxSplitsButTheIrDoesNot_LeavesTheStatementToTheSyntaxPasses()
    {
        var (body, symbol, model) = Method("EarlierArgumentOfACallThatDoesNotSplit");
        var prepared = AstLoweringPipeline.Prepare(body, false, model);
        var pair = Invocation(prepared, "Pair");
        var annotated = prepared.ReplaceNode(pair,
            pair.WithAdditionalAnnotations(new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind)));

        var (asIs, asIsOutcomes) = Rewrite(body, symbol, model, prepared);
        var (mismatched, outcomes) = Rewrite(body, symbol, model, annotated);

        Assert.True(Assert.Single(asIsOutcomes).IsLowered);
        Assert.Equal(IrSpillApplySkip.SplitAnnotationMismatch, Assert.Single(outcomes).Skip);
        Assert.NotEqual(asIs.Statements.Count, mismatched.Statements.Count);
        Assert.DoesNotContain(mismatched.DescendantNodes(),
            static node => node.HasAnnotations(LoweringAnnotations.IrLoweredAnnotationKind));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Rewrite_SplitOfTheIrWithoutItsAnnotation_LeavesTheStatementToTheSyntaxPasses()
    {
        var (body, symbol, model) = Method("TwoSplitStatements");
        var prepared = AstLoweringPipeline.Prepare(body, false, model);
        var first = Invocation(prepared, "Suspend");
        var unannotated = prepared.ReplaceNode(first,
            first.WithoutAnnotations(LoweringAnnotations.SuspendingCallAnnotationKind));

        var (_, outcomes) = Rewrite(body, symbol, model, unannotated);

        Assert.Collection(outcomes,
            static outcome => Assert.Equal(IrSpillApplySkip.SplitAnnotationMismatch, outcome.Skip),
            static outcome => Assert.True(outcome.IsLowered));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Rewrite_PreparedStatementNotTheSource_LeavesItToTheSyntaxPasses()
    {
        var (body, symbol, model) = Method("TwoSplitStatements");
        var prepared = AstLoweringPipeline.Prepare(body, false, model);
        var index = prepared.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .First(static literal => literal.Token.ValueText == "0");
        var changed = prepared.ReplaceNode(index,
            index.WithToken(SyntaxFactory.Literal(5)).WithTriviaFrom(index));

        var (_, outcomes) = Rewrite(body, symbol, model, changed);

        Assert.Collection(outcomes,
            static outcome => Assert.Equal(IrSpillApplySkip.PreparedSyntaxDiffers, outcome.Skip),
            static outcome => Assert.True(outcome.IsLowered));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void HoistSplitPointsWithIr_OperandWithoutANaturalType_LeavesItToTheSyntaxPasses()
    {
        var (body, symbol, model) = Method("OperandWithoutNaturalType");

        AstLoweringPipeline.HoistSplitPointsWithIr(body, false, model, symbol, out var outcomes);

        Assert.Equal(IrSpillApplySkip.NoNaturalType, Assert.Single(outcomes).Skip);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void HoistSplitPointsWithIr_OperandInsideACheckedExpression_LeavesItToTheSyntaxPasses()
    {
        var (body, symbol, model) = Method("IndexCheckedArithmetic", PlannerCases);

        AstLoweringPipeline.HoistSplitPointsWithIr(body, false, model, symbol, out var outcomes);

        Assert.Equal(IrSpillApplySkip.OverflowContextOperand, Assert.Single(outcomes).Skip);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("TwoSplitStatements")]
    [InlineData("MixedIrAndLoopSplits")]
    public void HoistSplitPointsWithIr_SeveralStatementsWithTemporaries_GiveEveryTemporaryItsOwnName(string kernel)
    {
        var (body, symbol, model) = Method(kernel);

        var hoisted = AstLoweringPipeline.HoistSplitPointsWithIr(body, false, model, symbol, out var outcomes);

        var names = hoisted.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Select(static declarator => declarator.Identifier.ValueText)
            .Where(static name => name.StartsWith("__gsharp_", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(outcomes, static outcome => outcome.IsLowered);
        Assert.True(names.Length >= 4, $"Only {names.Length} temporaries: {string.Join(", ", names)}");
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Lower_CheckOfAnElementBeforeASplit_KeepsItsTemporaryOutOfTheFrame()
    {
        var (body, symbol, model) = Method("CheckOfAFieldTarget");
        AstLoweringPipeline.HoistSplitPointsWithIr(body, false, model, symbol, out var outcomes);
        var check = Assert.Single(Assert.Single(outcomes).Printed.OfType<BlockSyntax>());
        var checkName = Assert.Single(check.DescendantNodes().OfType<VariableDeclaratorSyntax>()).Identifier.ValueText;

        LoweredStateMachine lowered;
        using (AstLoweringPipeline.WithIrSplitLowering())
        {
            lowered = AstLoweringPipeline.Lower(
                body.Parent as MethodDeclarationSyntax ?? throw new InvalidOperationException("No method."),
                model,
                symbol,
                isDeviceFunction: false
            );
        }

        var fields = lowered.ContextFields.Select(static field => field.Identifier.ValueText).ToArray();
        var (localInits, regularFields, _, _) =
            ManagedContextFields.ProcessContextFields(lowered.ContextFields, model, symbol.Name);
        var managedFields = localInits.Concat(regularFields)
            .Select(static field => field.Declarator.Identifier.ValueText)
            .ToArray();
        Assert.StartsWith(GSharpInternalNames.Lowering.ArgumentTempPrefix, checkName, StringComparison.Ordinal);
        Assert.Contains(fields, static name => name.StartsWith(GSharpInternalNames.Lowering.CallTempPrefix,
            StringComparison.Ordinal));
        Assert.DoesNotContain(checkName, fields);
        Assert.DoesNotContain(checkName, managedFields);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Apply_OperandWithoutSyntaxOfItsOwnBeforeASplit_IsSkipped()
    {
        var plan = PlanOfCase("EarlierArgumentOfACallThatDoesNotSplit");
        var spilled = (IrSpillValue)plan.Steps[0];
        var synthesized = new IrDefaultValue(spilled.Node.Type, IrCategory.Value, spilled.Node.Source.AsSynthesized());

        var application = IrSpillRewriter.Apply(new IrStatementSpillPlan(plan.Statement, plan.Fallback,
            plan.Steps.SetItem(0, spilled with { Node = synthesized })));

        Assert.Equal(IrSpillApplySkip.SynthesizedOperand, application.Skip);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Apply_ImplicitBuiltInConversionBeforeASplit_IsWritten()
    {
        var plan = PlanOfCase("EarlierArgumentOfACallThatDoesNotSplit");
        var spilled = (IrSpillValue)plan.Steps[0];
        var conversion = new IrConversion(spilled.Node,
            new IrOperator<IrConversionKind>(IrConversionKind.Numeric, null, false), true, spilled.Node.Type,
            IrCategory.Value, default, spilled.Node.Source.AsSynthesized());

        var application = IrSpillRewriter.Apply(new IrStatementSpillPlan(plan.Statement, plan.Fallback,
            plan.Steps.SetItem(0, spilled with { Node = conversion })));

        Assert.Equal(IrSpillApplySkip.None, application.Skip);
    }

    private static string Describe(IrSplitLoweringOutcome outcome) =>
        outcome.IsLowered ? "written by the IR"
        : outcome.Fallback != IrSpillFallback.None ? "fallback " + outcome.Fallback
        : "skip " + outcome.Skip;

    private static (CSharpSyntaxNode Body, IMethodSymbol Symbol, SemanticModel Model) Method(string name,
        string file = Cases)
    {
        var compilation = IrTestCases.Compile(file);
        var tree = CasesTree(compilation);
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == name);
        var body = (CSharpSyntaxNode?)declaration.Body ?? declaration.ExpressionBody ??
                   throw new InvalidOperationException($"{name} has no body.");
        var symbol = model.GetDeclaredSymbol(declaration) ??
                     throw new InvalidOperationException($"{name} has no symbol.");
        return (body, symbol, model);
    }

    private static IEnumerable<(MethodDeclarationSyntax Declaration, SemanticModel Model)> Declarations(string file)
    {
        var compilation = IrTestCases.Compile(file);
        var tree = CasesTree(compilation);
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(declaration => (declaration, model));
    }

    // The tree of the cases file in its compilation, the one that declares the kernels
    private static SyntaxTree CasesTree(Compilation compilation) =>
        compilation.SyntaxTrees.Single(static tree => tree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Any(static type => type.Identifier.ValueText == "Cases"));

    private static IrStatementSpillPlan PlanOfCase(string name)
    {
        var (_, symbol, model) = Method(name);
        var built = IrBuilder.TryBuild(symbol, model.Compilation, CancellationToken.None) ??
                    throw new InvalidOperationException($"No IR for {name}.");
        return IrSpillPlanner.Plan(IrClassifier.Classify(built, model.Compilation), model.Compilation)
            .Statements
            .Single(static plan => plan.Fallback == IrSpillFallback.None && plan.Statement is IrExpressionStatement);
    }

    private static InvocationExpressionSyntax Invocation(BlockSyntax block, string name) =>
        block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(invocation => invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: var callee } &&
                                 callee == name);

    private static (BlockSyntax Block, IReadOnlyList<IrSplitLoweringOutcome> Outcomes) Rewrite(
        CSharpSyntaxNode body, IMethodSymbol symbol, SemanticModel model, BlockSyntax prepared)
    {
        var pass = new IrSplitLoweringPass(model, symbol, body, new LoweringTempNames());
        return (pass.Rewrite(prepared), pass.Outcomes);
    }
}
