using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// On the C++ dialect the context of a kernel holds the frame of every call that waits at a barrier. A frame that holds
/// a private nested type is private to the function's class, and kernels of other classes hold it through the entries
/// generated beside it; a frame that would hold a file-local type is not emitted, and a kernel that reaches one, directly
/// or through another device function, gets GSH0119 (a warning: its managed launch throws, the GPU and the native CPU
/// path run it). The generated C# compiles either way (#1835).
/// </summary>
[Category(TestCategory.Compiler)]
public class SuspendingCallFrameDiagnosticsTests
{
    private const string TestCase = "Analyzers/SuspendingCallFrameSadPath.cs";

    // The two kernels that reach the function whose frame would hold a file-local type (directly and through another
    // device function), the file-local type where that function names it, and the call in the relay; none of the
    // kernels whose callees' frames hold a private type
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelCall_FrameWithAFileLocalType_ReportsGsh0119()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        Assert.Equal(
            [
                (Analyzer, 47, 24, 47, 39),
                (Analyzer, 50, 24, 50, 46),
                (Analyzer, 63, 9, 63, 12),
                (Analyzer, 63, 25, 63, 39),
                (Analyzer, 73, 25, 73, 31)
            ],
            diagnostics.Ranges(GSharpDiagnostics.FileLocalTypeInManagedCopy.Id));
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
