using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A call from GPU code of a method of another assembly that the dialect does not implement (GSH0109): generic math
/// members of the numeric types, unmapped overloads of Math and MathF, a Vector3 method, and a Gpu macro whose body is
/// a method group. Mapped calls, device functions of other assemblies, local functions and the calls that rules write
/// are not reported.
/// </summary>
[Category(TestCategory.Compiler)]
public class UnmappedCallDiagnosticsTests
{
    private const string TestCase = "Diagnostics/UnmappedCall.cs";

    // float.Clamp, int.Max, Math.Max(long, long), MathF.Cbrt, int.Abs and short.Abs (lines 24 to 29), Vector3.Dot
    // (line 37) and Gpu.SingleThread(Body) (line 45): the whole call, once, from the analyzer
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnmappedCall_LibraryMethodWithoutMapping_ReportsGsh0109AtCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 24, 20, 24, 45),
                (Analyzer, 25, 20, 25, 36),
                (Analyzer, 26, 20, 26, 38),
                (Analyzer, 27, 20, 27, 36),
                (Analyzer, 28, 20, 28, 33),
                (Analyzer, 29, 20, 29, 35),
                (Analyzer, 37, 20, 37, 43),
                (Analyzer, 45, 13, 45, 35)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id));
    }

    // The method with its parameter types, the dialect, and what the dialect implements instead
    [Theory]
    [InlineData("Cpu", "'float.Clamp(float, float, float)'", "; use Math.Clamp(float, float, float) instead")]
    [InlineData("Cpu", "'int.Max(int, int)'", "; use Math.Max(int, int) instead")]
    [InlineData("Cpu", "'int.Abs(int)'", "; use Math.Abs(int) instead")]
    [InlineData("Cpu", "'Math.Max(long, long)'",
        "; the dialect implements Math.Max(double, double), Math.Max(float, float), Math.Max(int, int)")]
    [InlineData("Cpu", "'short.Abs(short)'",
        "; the dialect implements Math.Abs(double), Math.Abs(float), Math.Abs(int), MathF.Abs(float)")]
    [InlineData("Cpu", "'MathF.Cbrt(float)'", "cannot be called from GPU code")]
    [InlineData("Cpu", "'Gpu.SingleThread(Action)'",
        "; Gpu.SingleThread takes its body as a lambda; a method group or a delegate is not supported yet")]
    [InlineData("Cuda", "'int.Max(int, int)'", "; use Math.Max(int, int) instead")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnmappedCall_DiagnosticMessage_NamesTheDialectAndTheAlternative(string dialect, string method,
        string advice)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);
        var dialectName = dialect == "Cpu" ? "StandardCppDialect" : "GSharpCudaDialect";

        var message = Assert.Single(
            diagnostics,
            d => d.Id == GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id && d.Message.Contains(method)).Message;
        Assert.Contains($"has no implementation in the '{dialectName}' dialect", message);
        Assert.EndsWith(advice, message);
    }

    // Math.Abs(int), Math.Max(int, int), a [BclIntrinsicMap] method, ImmutableArray<T>.AsSpan(), a [GpuDeviceFunction]
    // of GpuMath, a local function and Gpu.SingleThread with a lambda body
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task MappedOrEmittedCall_ValidTargets_IsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.DoesNotContain(
            diagnostics,
            static d => d.Id == GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id && d.StartLine >= 56);
    }

    // Without the analyzers, as in the projects under tests/ (#463), the generator states each failure in the native
    // source with its place in the user's file, in every dialect
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnmappedCall_GeneratedSource_StatesGsh0109WithLocation(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.Equal(
            [
                ("(24,20): GSH0109", true),
                ("(25,20): GSH0109", true),
                ("(26,20): GSH0109", true),
                ("(27,20): GSH0109", true),
                ("(28,20): GSH0109", true),
                ("(29,20): GSH0109", true),
                ("(37,20): GSH0109", true),
                ("(45,13): GSH0109", true)
            ],
            KernelDiagnostics.FindStatedFailures(run)
                .Where(static f => f.Failure.EndsWith(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id))
                .Order());
    }
}
