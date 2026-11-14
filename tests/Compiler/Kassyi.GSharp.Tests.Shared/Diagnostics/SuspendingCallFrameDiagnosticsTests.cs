using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// On the C++ dialect the context of a kernel holds the frame of every call that waits at a barrier. A frame that holds
/// a private nested type is private to the function's class, and kernels of other classes hold it through the entries
/// generated beside it; a frame that would hold a file-local type is not emitted, and a kernel that reaches one gets
/// GSH0115 at the call. The generated C# compiles either way (#1835).
/// </summary>
[Category(TestCategory.Compiler)]
public class SuspendingCallFrameDiagnosticsTests
{
    private const string TestCase = "Analyzers/SuspendingCallFrameSadPath.cs";

    // The direct call and the call through a device function of a function whose frame would hold a file-local type;
    // none of the calls of functions whose frames hold a private type, from whichever class
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelCall_FrameItCannotHold_ReportsGsh0115AtTheCalledName()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        Assert.Equal(
            [
                (Analyzer, 47, 93, 47, 107),
                (Analyzer, 50, 99, 50, 105)
            ],
            diagnostics.Ranges(GSharpDiagnostics.SuspendingCallFrameUnreachable.Id));
    }

    [Theory]
    [InlineData("'FileLocalHolder.HoldsFileLocal': its frame would hold the file-local type 'FileLocalValue'")]
    [InlineData("'FileLocalRelay.Relays': it calls 'FileLocalHolder.HoldsFileLocal', whose frame cannot be " +
                "generated: its frame would hold the file-local type 'FileLocalValue'")]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelCall_FrameItCannotHold_MessageNamesTheReason(string message)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        Assert.Single(diagnostics.Ranges(GSharpDiagnostics.SuspendingCallFrameUnreachable.Id, message));
    }

    // The CUDA kernels have no managed state machine; a device function's frame that cannot be emitted is left out
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task CudaDialect_FrameItCannotHold_ReportsNothing()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cuda");

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.SuspendingCallFrameUnreachable.Id));
    }

    // The private frames are held as objects and driven through their entries; the frames that cannot be emitted are
    // named nowhere
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task GeneratedCode_FrameItCannotHold_CompilesWithoutErrors(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.Empty(run.Output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString()));
    }
}
