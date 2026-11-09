using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A call from GPU code of a method of the project that G# does not compile for the GPU (GSH0107, #891): a static
/// method without <c>[GpuDeviceFunction]</c> of a struct (generic or not) or of a class, and an instance method of a
/// struct. Marked methods and BCL intrinsics are not reported.
/// </summary>
[Category(TestCategory.Compiler)]
public class UnmarkedMethodCallDiagnosticsTests
{
    private const string TestCase = "Diagnostics/UnmarkedMethodCall.cs";

    // Box.Make (line 54), box.Set (line 55), Helpers.Twice (line 56), GBox<int>.Make (line 57) and the [Conditional]
    // method whose symbol the file defines (line 61) in the kernel, and Helpers.Twice in a device function (line 65).
    // Line 58 calls marked methods and Math intrinsics, line 59 a [BclIntrinsicMap] method, line 60 a [Conditional]
    // method that C# removes
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CallOfUnmarkedMethod_ReportsGsh0107AtTheCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 54, 23, 54, 34),
                (Analyzer, 55, 13, 55, 23),
                (Analyzer, 56, 25, 56, 49),
                (Analyzer, 57, 25, 57, 42),
                (Analyzer, 61, 13, 61, 36),
                (Analyzer, 65, 44, 65, 60)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id));
    }

    // Calls that lowering copies into the kernel: the iterator body (line 26) and the foreach body (line 35); and
    // method groups that the device code would name: a StackLinq stage (line 42) and a delegate (line 43)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_LoweredCallOrMethodGroupOfUnmarkedMethod_ReportsGsh0107AtTheName(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnmarkedMethodCallLowered.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 26, 30, 26, 40),
                (Analyzer, 35, 30, 35, 43),
                (Analyzer, 42, 59, 42, 67),
                (Analyzer, 43, 32, 43, 39)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id));
    }

    [Theory]
    [InlineData("'Box.Make'", "add [GpuDeviceFunction]")]
    [InlineData("'Helpers.Twice'", "add [GpuDeviceFunction]")]
    [InlineData("'GBox.Make'", "add [GpuDeviceFunction]")]
    [InlineData("'Box.Set'", "instance methods of structs are not supported in GPU code yet (GSH0066)")]
    [InlineData("'Helpers.TraceOn'", "add [GpuDeviceFunction]")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CallOfUnmarkedMethod_StatesWhatToDo(string method, string advice)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        var messages = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id && d.Message.Contains(method))
            .Select(static d => d.Message)
            .ToList();
        Assert.NotEmpty(messages);
        Assert.All(messages, message => Assert.Contains(advice, message));
    }

    // The marked methods and Math intrinsics (line 58), the [BclIntrinsicMap] method (line 59) and the call C# removes
    // (line 60) are not reported
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_CallOfMarkedMethodOrIntrinsic_IsNotReported()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        Assert.DoesNotContain(
            diagnostics,
            static d => d.Id == GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id && d.StartLine is >= 58 and <= 60);
        Assert.DoesNotContain(
            diagnostics,
            static d => d.Id == GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id &&
                        (d.Message.Contains("Marked") || d.Message.Contains("Thrice") || d.Message.Contains("Math") ||
                         d.Message.Contains("Lzc") || d.Message.Contains("TraceOff")));
    }
}
