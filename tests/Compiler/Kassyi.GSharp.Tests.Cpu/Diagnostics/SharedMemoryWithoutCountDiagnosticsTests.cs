using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>SharedMemory&lt;T&gt;</c> without an element count (#643). The parameterless constructor is an error of the C#
/// compiler (CS0619, which no pragma can lower, so a test never runs the constructor). Every other way to reach one is
/// reported as unsupported syntax (GSH0003) on every dialect: <c>default</c> in each position, a local without an
/// initializer, a struct local that holds one and is <c>default</c> or <c>new T()</c> with no assignment, and a
/// generic method that uses <c>default</c> of a type parameter instantiated with it. Each form is one test, run on
/// both dialects.
/// </summary>
[Category(TestCategory.Compiler)]
public class SharedMemoryWithoutCountDiagnosticsTests
{
    private const string SadPath = "Diagnostics/SharedMemoryWithoutCount.cu.cs";

    private static readonly Lazy<Task<ImmutableArray<ReportedDiagnostic>>> s_cpu =
        new(static () => KernelDiagnostics.RunTestCaseAsync(SadPath, "Cpu"));

    private static readonly Lazy<Task<ImmutableArray<ReportedDiagnostic>>> s_cuda =
        new(static () => KernelDiagnostics.RunTestCaseAsync(SadPath, "Cuda"));

    private const string DefaultReason =
        "SharedMemory<T> without an element count (default); declare it with new SharedMemory<T>(count)";

    // The test case has one kernel or device function per form; the position of the form is in the name of the test
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task DefaultLiteral_LocalInitializer_ReportsGsh0003AtDefault(string dialect) =>
        AssertReportedAsync(dialect, DefaultReason, 29, 31, 29, 38);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task DefaultExpression_LocalInitializer_ReportsGsh0003AtExpression(string dialect) =>
        AssertReportedAsync(dialect, DefaultReason, 37, 17, 37, 45);

    // Held across a barrier, the declaration is part of the CPU state machine's context
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task DefaultLiteral_HeldAcrossBarrier_ReportsGsh0003AtDefault(string dialect) =>
        AssertReportedAsync(dialect, DefaultReason, 46, 31, 46, 38);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task DefaultLiteral_DeviceFunctionReturn_ReportsGsh0003AtDefault(string dialect) =>
        AssertReportedAsync(dialect, DefaultReason, 52, 56, 52, 63);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task DefaultLiteral_ConditionalBranch_ReportsGsh0003AtDefault(string dialect) =>
        AssertReportedAsync(dialect, DefaultReason, 58, 51, 58, 58);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task DefaultLiteral_SwitchExpressionArm_ReportsGsh0003AtDefault(string dialect) =>
        AssertReportedAsync(dialect, DefaultReason, 69, 18, 69, 25);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task DefaultLiteral_CallArgument_ReportsGsh0003AtDefault(string dialect) =>
        AssertReportedAsync(dialect, DefaultReason, 78, 25, 78, 32);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task DefaultLiteral_ParameterDefaultValue_ReportsGsh0003AtDefault(string dialect) =>
        AssertReportedAsync(dialect, DefaultReason, 82, 67, 82, 74);

    // The declarator, which is the name of the local
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task Local_WithoutInitializer_ReportsGsh0003AtDeclarator(string dialect) =>
        AssertReportedAsync(dialect, GpuCodeConstraints.SharedMemoryLocalWithoutInitializer, 88, 27, 88, 28);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task StructLocal_DefaultWithSharedMemoryField_ReportsGsh0003AtDefault(string dialect) =>
        AssertReportedAsync(dialect, GpuCodeConstraints.SharedMemoryHolderWithoutMemory, 97, 37, 97, 44);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task StructLocal_NewWithSharedMemoryField_ReportsGsh0003AtCreation(string dialect) =>
        AssertReportedAsync(dialect, GpuCodeConstraints.SharedMemoryHolderWithoutMemory, 105, 22, 105, 46);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task StructLocal_DefaultWithSharedMemoryProperty_ReportsGsh0003AtDefault(string dialect) =>
        AssertReportedAsync(dialect, GpuCodeConstraints.SharedMemoryHolderWithoutMemory, 113, 45, 113, 52);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public Task GenericMethod_DefaultOfTypeParameterInstantiatedWithSharedMemory_ReportsGsh0003AtCall(string dialect) =>
        AssertReportedAsync(dialect, GpuCodeConstraints.SharedMemoryGenericDefault, 121, 17, 121, 42);

    // AcceptedKernel (a count, the default of an int, a holder whose memory is assigned, by an object initializer or by
    // a copy of another holder) adds nothing to the thirteen forms above
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task AcceptedForms_CountOrAssignment_ReportNothing(string dialect)
    {
        var diagnostics = await RunAsync(dialect);

        Assert.Equal(13, diagnostics.Count(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    // new SharedMemory<int>() and the target-typed new() bind to the parameterless constructor, which is obsolete as an
    // error, so the C# compiler rejects them before any G# analysis
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void NewWithoutArguments_ParameterlessConstructor_ReportsCs0619()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            public static class Host
            {
                public static void Declare()
                {
                    var a = new SharedMemory<int>();
                    SharedMemory<float> b = new();
                    var c = new SharedMemory<int>(4);
                }
            }
            """;

        var errors = RoslynTestUtility.CreateCompilation(Source)
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => (d.Id, d.Location.GetLineSpan().StartLinePosition.Line + 1))
            .ToList();

        Assert.Equal([("CS0619", 7), ("CS0619", 8)], errors);
    }

    private static Task<ImmutableArray<ReportedDiagnostic>> RunAsync(string dialect) =>
        dialect == "Cuda" ? s_cuda.Value : s_cpu.Value;

    // The range holds exactly once, with the reason of the form in the message
    private static async Task AssertReportedAsync(
        string dialect,
        string reason,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        var diagnostics = await RunAsync(dialect);

        Assert.Single(
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, reason),
            (Analyzer, startLine, startColumn, endLine, endColumn));
    }
}
