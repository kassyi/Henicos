#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Cpu.Generator;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ManagedStackModelGeneratorTests
{
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    private static readonly SyntaxTree s_tree =
        RoslynTestUtility.GetTestCaseContext("Generators/ManagedStackModelCases.cs").Tree;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void BarrierModel_UnsetStateMachineOrUnknown_ProducesIdenticalSources()
    {
        var unset = Run(barrierModel: null);
        var stateMachine = Run(barrierModel: "StateMachine");
        var unknown = Run(barrierModel: "Stak");

        AssertSameSources(unset, stateMachine);
        AssertSameSources(unset, unknown);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BarrierModel_StackCaseInsensitive_MatchesAndDiffersFromDefault()
    {
        var unset = Run(barrierModel: null);
        var upper = Run(barrierModel: "Stack");
        var lower = Run(barrierModel: "stack");

        AssertSameSources(upper, lower);
        Assert.NotEqual(
            unset.Select(static s => (s.HintName, s.SourceText)),
            upper.Select(static s => (s.HintName, s.SourceText)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task BarrierModel_Stack_EmitsStackFormsForBarriersAndWarpCollectives()
    {
        var sources = Run(barrierModel: "Stack");
        var combined = string.Join(
            "\n\n",
            sources
                .Where(static s => s.HintName.EndsWith("StackModelKernels.g.cs", StringComparison.Ordinal) ||
                                   s.HintName.EndsWith("StackModelKernels.DeviceStateMachines.g.cs",
                                       StringComparison.Ordinal))
                .Select(static s => $"// === {s.HintName} ===\n{s.SourceText}"));

        return Verifier.Verify(combined, "cs")
            .UseDirectory("Snapshots")
            .UseFileName("Snap.ManagedStackModel_Stack_Cpu");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void StackFormThatCannotBeMade_EitherModel_BuildsAndTheStackModelListsTheFallback()
    {
        var unset = Run(barrierModel: null, s_fallbackTree);
        var stack = Run(barrierModel: "Stack", s_fallbackTree);

        Assert.DoesNotContain(unset, static s => s.HintName == ManagedStackPipeline.FailuresHintName);
        var failures = Assert.Single(stack, static s => s.HintName == ManagedStackPipeline.FailuresHintName);
        Assert.Contains("FallbackKernels.PassesSuspendingGroupKernel: InvalidOperationException", failures.SourceText,
            StringComparison.Ordinal);
        // A device function whose form cannot be made takes the forms of its callers along, up the whole chain
        Assert.Contains("FallbackKernels.Inner: InvalidOperationException", failures.SourceText,
            StringComparison.Ordinal);
        Assert.Contains("FallbackKernels.Middle: calls a function without a stack form", failures.SourceText,
            StringComparison.Ordinal);
        Assert.Contains("FallbackKernels.Outer: calls a function without a stack form", failures.SourceText,
            StringComparison.Ordinal);
        Assert.Contains("FallbackKernels.CallsChainKernel: calls a function without a stack form", failures.SourceText,
            StringComparison.Ordinal);
        // The kernel keeps its state machine: no stack form is emitted for it
        Assert.DoesNotContain(stack, static s => s.SourceText.Contains(
            GSharpInternalNames.StackModel.MethodPrefix, StringComparison.Ordinal));
        AssertSameSources(
            unset,
            stack.Where(static s => s.HintName != ManagedStackPipeline.FailuresHintName).ToArray());
    }

    private static readonly SyntaxTree s_fallbackTree =
        RoslynTestUtility.GetTestCaseContext("Generators/ManagedStackFallbackCases.cs").Tree;

    private static (string HintName, string SourceText)[] Run(string? barrierModel) => Run(barrierModel, s_tree);

    private static (string HintName, string SourceText)[] Run(string? barrierModel, SyntaxTree tree)
    {
        var compilation = CSharpCompilation.Create("ManagedStackModelCase")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(tree);
        var options = barrierModel is null
            ? null
            : new Dictionary<string, string> { ["build_property.GSharpManagedBarrierModel"] = barrierModel };

        var driver = CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: s_parseOptions,
                optionsProvider: new TestAnalyzerConfigOptionsProvider("Cpu", options))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        return result.GeneratedSources
            .OrderBy(static s => s.HintName, StringComparer.Ordinal)
            .Select(static s => (s.HintName, s.SourceText.ToString()))
            .ToArray();
    }

    private static void AssertSameSources((string HintName, string SourceText)[] expected,
        (string HintName, string SourceText)[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].HintName, actual[i].HintName);
            Assert.Equal(expected[i].SourceText, actual[i].SourceText);
        }
    }
}
