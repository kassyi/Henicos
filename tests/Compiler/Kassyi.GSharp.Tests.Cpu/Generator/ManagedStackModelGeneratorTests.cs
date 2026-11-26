#nullable enable
using System.Collections.Generic;
using System.IO;
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

        Assert.DoesNotContain(unset, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        var failures = Assert.Single(stack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.Contains("// managed\tFallbackKernels.PassesSuspendingGroupKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        // The form fails because the function it names has none (ManagedRouting.GetTargetPrefix)
        Assert.Contains("'BarrierStep' without a stack form", failures.SourceText, StringComparison.Ordinal);
        // A device function whose form cannot be made takes the forms of its callers along, up the whole chain
        Assert.Contains("// managed\tFallbackKernels.Inner\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.Middle\tCallsFunctionWithoutForm: calls a function without a stack form",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.Outer\tCallsFunctionWithoutForm: calls a function without a stack form",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackKernels.CallsChainKernel\tCallsFunctionWithoutForm: calls a function without a stack form",
            failures.SourceText, StringComparison.Ordinal);
        // A using declaration that a goto of the reconvergence regions would pass (CS8648) keeps the kernel on its
        // state machine, with the reason
        Assert.Contains("// managed\tFallbackKernels.UsingAfterContinueKernel\tUsingDeclarationWithActiveMask",
            failures.SourceText, StringComparison.Ordinal);
        // The kernels and the functions of the chain keep their state machines: no stack form is emitted for them, and
        // the kernel file is the default build's. Only AddShuffled, the target of the operator, has a form of its own
        foreach (var name in new[]
                 {
                     "PassesSuspendingGroupKernel", "BarrierStep", "CallsChainKernel", "Outer", "Middle", "Inner",
                     "UsingAfterContinueKernel"
                 })
        {
            var formName = GSharpInternalNames.StackModel.MethodPrefix +
                           "ManagedStackFallbackGeneratorCases__FallbackKernels__" + name + "(";
            Assert.DoesNotContain(stack, s => s.SourceText.Contains(formName, StringComparison.Ordinal));
        }

        const string AddShuffledForm =
            GSharpInternalNames.StackModel.MethodPrefix + "ManagedStackFallbackGeneratorCases__FallbackKernels__AddShuffled(";
        Assert.Contains(stack, static s => s.SourceText.Contains(AddShuffledForm, StringComparison.Ordinal));
        var kernelFile = Assert.Single(unset, static s => s.HintName.EndsWith("__FallbackKernels.g.cs",
            StringComparison.Ordinal));
        Assert.Equal(kernelFile, Assert.Single(stack, static s => s.HintName.EndsWith("__FallbackKernels.g.cs",
            StringComparison.Ordinal)));
    }

    // An operator macro whose target reaches a warp collective, after a block barrier, in a kernel that has a
    // collective of its own, and in a kernel that reaches Warp.ActiveMask() (whose stack form writes the lowered body)
    // with a target that reaches Warp.ActiveMask(): the stack forms of the kernels cannot be made, the build still
    // compiles (Run checks it), the stack model lists the reason, and the kernels keep the launchers of the default
    // build
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void OperatorMacroReachingAWarpCollective_StackModel_KeepsTheKernelsOnTheStateMachine()
    {
        var unset = Run(barrierModel: null, s_macroTree);
        var stack = Run(barrierModel: "Stack", s_macroTree);

        Assert.DoesNotContain(unset, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        var failures = Assert.Single(stack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.Contains("// managed\tMacroKernels.MacroAfterBarrierKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tMacroKernels.MacroAndShuffleKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tMacroKernels.MacroAndActiveMaskKernel\tStackFormGenerationFailed: InvalidOperationException",
            failures.SourceText, StringComparison.Ordinal);
        Assert.Contains("through an operator macro", failures.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(stack, static s => s.SourceText.Contains("ManagedStackRunner.Run(",
            StringComparison.Ordinal));
        var kernelFile = Assert.Single(unset, static s => s.HintName.EndsWith("MacroKernels.g.cs",
            StringComparison.Ordinal));
        Assert.Equal(kernelFile, Assert.Single(stack, static s => s.HintName.EndsWith("MacroKernels.g.cs",
            StringComparison.Ordinal)));
    }

    // The kernels whose locals the lowering for the stacks leaves out of the scope of their uses, untyped or not
    // definitely assigned: under the stack model every one of them gets a stack form that compiles (Run checks that the
    // output has no error) and runs on the stack scheduler, with no fallback
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalsOutOfTheirScope_StackModel_CompileAndRunOnTheStacks()
    {
        var tree = CSharpSyntaxTree.ParseText(
            File.ReadAllText(RepositoryPaths.Combine(
                "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Runtime", "StackBodyLocalsKernels.cu.cs")),
            s_parseOptions,
            "StackBodyLocalsKernels.cu.cs");

        var stack = Run(barrierModel: "Stack", tree);

        Assert.DoesNotContain(stack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        var kernelFile = Assert.Single(stack, static s => s.HintName.EndsWith("__StackBodyLocalsKernels.g.cs",
            StringComparison.Ordinal));
        var kernels = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static m => m.AttributeLists.SelectMany(static l => l.Attributes)
                .Any(static a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "GpuKernel" }))
            .Select(static m => m.Identifier.ValueText)
            .ToArray();
        Assert.Equal(17, kernels.Length);
        foreach (var kernel in kernels)
        {
            Assert.Contains($"sharedMemBytes, \"StackBodyLocalsKernels.{kernel}\", true,", kernelFile.SourceText,
                StringComparison.Ordinal);
        }
    }

    // A scoped span declared without a value: its stack form declares it with default(Span<uint>), and the form holds no
    // C# error. Only the stack form is checked: the state machine of the same kernel does not compile yet (it writes
    // scoped twice), which is a problem of the default build of its own
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ScopedLocalWithoutValue_StackModel_FormDeclaresItWithTheDefaultOfItsType()
    {
        var tree = RoslynTestUtility.GetTestCaseContext("Generators/StackBodyScopedCases.cs").Tree;
        var compilation = CSharpCompilation.Create("ManagedStackModelCase")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(tree);
        CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: s_parseOptions,
                optionsProvider: new TestAnalyzerConfigOptionsProvider("Cpu",
                    new Dictionary<string, string> { ["build_property.GSharpManagedBarrierModel"] = "Stack" }))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var form = output.SyntaxTrees
            .SelectMany(static t => t.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Single(static m => m.Identifier.ValueText ==
                                GSharpInternalNames.StackModel.MethodPrefix +
                                "StackBodyScopedCases__ScopedKernels__ScopedSpanKernel");
        var declaration = form.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(static d => d.Identifier.ValueText == "s");
        var initializer = Assert.IsType<DefaultExpressionSyntax>(declaration.Initializer?.Value);
        Assert.IsNotType<ScopedTypeSyntax>(initializer.Type);
        Assert.Empty(output.GetSemanticModel(form.SyntaxTree)
            .GetDiagnostics(form.Span)
            .Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    private static readonly SyntaxTree s_macroTree =
        RoslynTestUtility.GetTestCaseContext("Generators/ManagedStackOperatorMacroCases.cs").Tree;

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
