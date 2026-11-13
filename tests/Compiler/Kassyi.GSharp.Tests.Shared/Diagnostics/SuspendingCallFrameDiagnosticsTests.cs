using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// On the C++ dialect the context of a kernel holds the frame of every call that waits at a barrier. A frame that holds a
/// private nested type is private to the function's class, and a frame that would hold a file-local type is not emitted:
/// a kernel that cannot hold the frame gets GSH0115 at the call, and the generated C# still compiles (#1835).
/// </summary>
[Category(TestCategory.Compiler)]
public class SuspendingCallFrameDiagnosticsTests
{
    private const string TestCase = "Analyzers/SuspendingCallFrameSadPath.cs";

    // The call from a kernel of another class, the call through a device function of another class and a frame with a
    // file-local type; not the kernels of the class that declares the private type and of a class nested in it
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelCall_FrameItCannotHold_ReportsGsh0115AtTheCalledName()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        Assert.Equal(
            [
                (Analyzer, 40, 85, 40, 97),
                (Analyzer, 43, 86, 43, 92),
                (Analyzer, 46, 93, 46, 107)
            ],
            diagnostics.Ranges(GSharpDiagnostics.SuspendingCallFrameUnreachable.Id));
    }

    [Theory]
    [InlineData("'FrameOwner.HoldsPrivate' here: its frame holds a type that only 'FrameOwner' can name")]
    [InlineData("'FrameRelay.Relays' here: it calls 'FrameOwner.HoldsPrivate', which 'FrameRelay' cannot call")]
    [InlineData("'FileLocalHolder.HoldsFileLocal' here: its frame would hold the file-local type 'FileLocalValue'")]
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

    // Without the frames it cannot hold, the generated C# names no frame that is missing or private elsewhere
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
