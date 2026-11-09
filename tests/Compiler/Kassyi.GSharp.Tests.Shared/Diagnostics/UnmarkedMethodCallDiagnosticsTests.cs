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

    // Box.Make (line 38), box.Set (line 39), Helpers.Twice (line 40), GBox<int>.Make (line 41) in the kernel, and
    // Helpers.Twice in a device function (line 46); line 42 calls only marked methods and Math intrinsics
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CallOfUnmarkedMethod_ReportsGsh0107AtTheCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 38, 23, 38, 34),
                (Analyzer, 39, 13, 39, 23),
                (Analyzer, 40, 25, 40, 49),
                (Analyzer, 41, 25, 41, 42),
                (Analyzer, 46, 44, 46, 60)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id));
    }

    [Theory]
    [InlineData("'Box.Make'", "add [GpuDeviceFunction]")]
    [InlineData("'Helpers.Twice'", "add [GpuDeviceFunction]")]
    [InlineData("'GBox.Make'", "add [GpuDeviceFunction]")]
    [InlineData("'Box.Set'", "instance methods of structs are not supported in GPU code yet (GSH0066)")]
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

    // The marked methods and the Math intrinsics of line 42 are not reported
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_CallOfMarkedMethodOrIntrinsic_IsNotReported()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        Assert.DoesNotContain(
            diagnostics,
            static d => d.Id == GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id && d.StartLine == 42);
        Assert.DoesNotContain(
            diagnostics,
            static d => d.Id == GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id &&
                        (d.Message.Contains("Marked") || d.Message.Contains("Thrice") || d.Message.Contains("Math")));
    }
}
