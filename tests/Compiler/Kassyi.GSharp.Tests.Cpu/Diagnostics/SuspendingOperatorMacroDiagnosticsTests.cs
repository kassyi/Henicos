#nullable enable
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>[GpuOperatorMacro]</c> operator whose target reaches a barrier or a warp collective cannot run on the CPU
/// state machine without a frame, so it reports GSH0062 at the operator span on all CPU configurations (default,
/// native Stack, managed Stack, both Stack) and on the CUDA dialect until the stack models support it. Inside a
/// struct member body that device code runs, it reports GSH0062 with the member context.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class SuspendingOperatorMacroDiagnosticsTests
{
    private const string TestCase = "Diagnostics/SuspendingOperatorMacro.cs";

    private static readonly string Id = GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id;

    private static readonly Lazy<Compilation> s_compilation = new(static () =>
        RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase)));

    private static string ExpectedMessage(string targetName, string context) =>
        $"'{targetName}' is a device function that reaches Gpu.SyncThreads() or a warp shuffle, " +
        $"so it cannot be called {context}. The CPU state machine splits a kernel or device function at such a " +
        "call only in the statements of its body, including the operands of '&&', '||' and '?:'.";

    [Theory]
    [InlineData("Cpu", null, null)]
    [InlineData("Cpu", "Stack", null)]
    [InlineData("Cpu", null, "Stack")]
    [InlineData("Cpu", "Stack", "Stack")]
    [InlineData("Cuda", null, null)]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingOperatorMacro_AcrossDialectsAndModels_ReportsGsh0062AtOperatorSpan(
        string dialect, string? nativeModel, string? managedModel)
    {
        var run = await RunCaseAsync(dialect, nativeModel, managedModel);
        var diagnostics = CollectReported(run);

        var gsh0062 = diagnostics
            .Where(d => d.Id == Id)
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ToArray();

        Assert.Equal(
            [
                (Analyzer, 60, 21, 60, 26),
                (Analyzer, 109, 17, 109, 22),
                (Analyzer, 121, 17, 121, 22),
                (Analyzer, 133, 17, 133, 22),
                (Analyzer, 149, 18, 149, 23),
                (Analyzer, 163, 17, 163, 23),
                (Analyzer, 198, 17, 198, 22)
            ],
            gsh0062.Select(static d => (d.Origin, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)).ToArray());

        Assert.Equal(ExpectedMessage("Add", "in a property accessor"), gsh0062[0].Message);
        Assert.Equal(ExpectedMessage("Add", "through an operator macro"), gsh0062[1].Message);
        Assert.Equal(ExpectedMessage("AddB", "through an operator macro"), gsh0062[2].Message);
        Assert.Equal(ExpectedMessage("Add", "through an operator macro"), gsh0062[3].Message);
        Assert.Equal(ExpectedMessage("AddToPlain", "through an operator macro"), gsh0062[4].Message);
        Assert.Equal(ExpectedMessage("AndActive", "through an operator macro"), gsh0062[5].Message);
        Assert.Equal(ExpectedMessage("AddB", "through an operator macro"), gsh0062[6].Message);

        // Case F (NonSuspendingMacroKernel, lines 169-177): no diagnostics
        Assert.DoesNotContain(diagnostics, static d => d.StartLine is >= 169 and <= 177);
    }

    [Theory]
    [InlineData("Cpu", null, null)]
    [InlineData("Cpu", "Stack", null)]
    [InlineData("Cpu", null, "Stack")]
    [InlineData("Cpu", "Stack", "Stack")]
    [InlineData("Cuda", null, null)]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingOperatorMacro_GeneratedSource_StatesEachFailureWhereAnalyzerReportsIt(
        string dialect, string? nativeModel, string? managedModel)
    {
        var run = await RunCaseAsync(dialect, nativeModel, managedModel);

        var stated = KernelDiagnostics.FindStatedFailures(run);
        Assert.NotEmpty(stated);
        Assert.All(stated, static f => Assert.True(f.IsReported, f.Failure));
        Assert.Equal(
            [
                ("(109,17): GSH0062", true),
                ("(121,17): GSH0062", true),
                ("(133,17): GSH0062", true),
                ("(149,18): GSH0062", true),
                ("(163,17): GSH0062", true),
                ("(198,17): GSH0062", true),
                ("(60,21): GSH0062", true)
            ],
            stated
                .Where(static f => f.Failure.EndsWith("GSH0062", StringComparison.Ordinal))
                .OrderBy(static f => f.Failure, StringComparer.Ordinal)
                .ToArray());

        var inputTrees = run.Input.SyntaxTrees.ToHashSet();
        var barrierMacroDeclarator = run.Output.SyntaxTrees
            .Where(t => !inputTrees.Contains(t))
            .SelectMany(static t => t.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>())
            .Single(static v => v.Identifier.ValueText == "BarrierMacroAfterBarrierKernel_Source");
        var literal = Assert.IsType<LiteralExpressionSyntax>(barrierMacroDeclarator.Initializer?.Value);
        var generatedSource = literal.Token.ValueText;
        Assert.Contains("#error", generatedSource, StringComparison.Ordinal);
        Assert.Contains("GSH0062", generatedSource, StringComparison.Ordinal);
    }

    private static Task<KernelDiagnosticsRun> RunCaseAsync(string dialect, string? nativeModel, string? managedModel)
    {
        Dictionary<string, string>? options = null;
        if (nativeModel != null || managedModel != null)
        {
            options = new Dictionary<string, string>();
            if (nativeModel != null)
            {
                options["build_property.GSharpNativeBarrierModel"] = nativeModel;
            }

            if (managedModel != null)
            {
                options["build_property.GSharpManagedBarrierModel"] = managedModel;
            }
        }

        return KernelDiagnostics.RunCompilationAsync(s_compilation.Value, dialect, options);
    }

    private static ImmutableArray<ReportedDiagnostic> CollectReported(KernelDiagnosticsRun run) =>
        run.Analyzer
            .Select(static d => ReportedDiagnostic.From(Analyzer, d))
            .Concat(run.Generator.Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d)))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ThenBy(static d => d.EndLine)
            .ThenBy(static d => d.EndColumn)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ThenBy(static d => d.Origin)
            .ThenBy(static d => d.Message, StringComparer.Ordinal)
            .ToImmutableArray();
}
