#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Targets;

/// <summary>
/// Diagnostic GSH0116: reports a warning when a kernel enables <c>[GpuKernel(AllowOversizedSharedMemory = true)]</c>
/// but no target GPU in <c>GSharpTargetArchitectures</c> has an oversized shared memory configuration.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class OversizedSharedMemoryDiagnosticTests
{
    private const string Id = "GSH0116";
    private const string ArchitecturesKey = "build_property.GSharpTargetArchitectures";
    private const string TestCasePath = "Diagnostics/OversizedSharedMemoryDiagnosticCases.cs";

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Analyzer_MarkedKernelWithTargetArchitecturesHavingNoOversized_WarnsAtAttributeArgument()
    {
        var diagnostics = await RunAsync("compute_86,sm_86");

        var warning = Assert.Single(diagnostics, d => d.Id == Id);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);

        var span = warning.Location.GetLineSpan().Span;
        // Line 8: [GpuKernel(AllowOversizedSharedMemory = true)]
        Assert.Equal(7, span.Start.Line);
        Assert.Equal(15, span.Start.Character);
        Assert.Equal(7, span.End.Line);
        Assert.Equal(48, span.End.Character);

        var message = warning.GetMessage();
        Assert.Contains("OversizedSharedMemoryDiagnosticCases.MarkedKernel", message);
        Assert.Contains("sm_86, sm_89", message);
        Assert.Contains("the property has no effect", message);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(null)]
    [InlineData("sm_107a")]
    [InlineData("sm_72")]
    [InlineData("compute_86,sm_86;sm_72")]
    public async Task Analyzer_TargetsWithOversizedOrUnknownOrUnspecified_DoesNotWarnForMarkedKernel(
        string? architectures)
    {
        var diagnostics = await RunAsync(architectures);

        Assert.DoesNotContain(diagnostics, d => d.Id == Id);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Analyzer_UnmarkedOrExplicitlyFalseKernel_DoesNotWarn()
    {
        var diagnostics = await RunAsync("compute_86,sm_86");

        Assert.DoesNotContain(diagnostics,
            static d => d.Id == Id && d.GetMessage().Contains("ExplicitlyUnmarkedKernel"));
        Assert.DoesNotContain(diagnostics,
            static d => d.Id == Id && d.GetMessage().Contains("DefaultKernel"));
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAsync(string? architectures)
    {
        var source = RoslynTestUtility.ReadTestCase(TestCasePath);
        var options = architectures is null
            ? null
            : new Dictionary<string, string> { [ArchitecturesKey] = architectures };

        return await GeneratorTestRunner.GetAnalyzerDiagnosticsAsync(
            source,
            new GpuKernelAnalyzer(),
            targetDialect: "Cuda",
            additionalOptions: options);
    }
}
