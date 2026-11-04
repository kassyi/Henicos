#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Kassyi.GSharp.Tools.FuzzDiff.Tests.MetamorphicTestSupport;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Tests for <see cref="MetamorphicStagePipeline"/>: a rewrite that adds a compile error is dropped, and the stages
/// after it work on the code as it was.
/// </summary>
public class FuzzMetamorphicStagePipelineTests
{
    private const string KernelName = "KernelPipeline";

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Apply_StageThatAddsCompileError_IsDiscarded()
    {
        var pipeline = MetamorphicStagePipeline.Create(ReadTestCase("StagePipelineKernel.cs"));
        string before = FuzzSyntaxHelpers.RenderFullNode(pipeline.Root);

        bool applied = pipeline.Apply(static (root, _) => ReplaceInitializerOfV(root, "\"text\""));

        // `int v = "text";` is CS0029; the pipeline must keep the code it had.
        // mutant: MetamorphicStagePipeline.Apply: drop the `nextErrorCount > _errorCount` check
        // input: a stage that replaces the initializer of `v` with a string literal
        Assert.False(applied);
        Assert.Equal(before, FuzzSyntaxHelpers.RenderFullNode(pipeline.Root));
        AssertNoCompileErrors(FuzzSyntaxHelpers.RenderFullNode(pipeline.Root));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Apply_StageAfterDiscardedStage_RewritesTheKeptCode()
    {
        var pipeline = MetamorphicStagePipeline.Create(ReadTestCase("StagePipelineKernel.cs"));

        bool broken = pipeline.Apply(static (root, _) => ReplaceInitializerOfV(root, "\"text\""));
        bool extracted = pipeline.Apply(static (root, model) =>
            MetamorphicSubexpressionExtractor.ExtractToLocal(root, model, seed: 1UL, KernelName));

        // The extraction runs on a model without errors, so it is not skipped and is kept.
        // mutant: MetamorphicStagePipeline.Apply: keep the broken root, so ExtractToLocal sees an error and does nothing
        // input: a breaking stage followed by ExtractToLocal on the pipeline kernel
        Assert.False(broken);
        Assert.True(extracted);
        string result = FuzzSyntaxHelpers.RenderFullNode(pipeline.Root);
        Assert.Contains("metaExtracted0", result, StringComparison.Ordinal);
        AssertNoCompileErrors(result);
    }

    // int v = <initializer>;
    private static CompilationUnitSyntax ReplaceInitializerOfV(CompilationUnitSyntax root, string initializer)
    {
        foreach (var node in root.DescendantNodes())
        {
            if (node is VariableDeclaratorSyntax { Identifier.ValueText: "v", Initializer: { } init })
            {
                return root.ReplaceNode(init.Value, SyntaxFactory.ParseExpression(initializer));
            }
        }

        throw new InvalidOperationException("The pipeline kernel has no local `v`.");
    }
}
