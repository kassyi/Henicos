using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>SharedMemory&lt;T&gt;</c> without an element count (#643): the parameterless constructor is an error of the C#
/// compiler (CS0619, which no pragma can lower, so the constructor itself is never run by a test), and <c>default</c>,
/// which the language cannot block, is reported as unsupported syntax (GSH0003) on every dialect.
/// </summary>
[Category(TestCategory.Compiler)]
public class SharedMemoryWithoutCountDiagnosticsTests
{
    private const string SadPath = "Diagnostics/SharedMemoryWithoutCount.cu.cs";

    private const string Reason =
        "SharedMemory<T> without an element count (default); declare it with new SharedMemory<T>(count)";

    // Line 13: the default literal. Line 21: default(SharedMemory<float>). Line 30: the default literal of a declaration
    // held across a barrier. Line 36: the default returned by a device function. AcceptedKernel (a count, and the
    // default of an int) is not reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Default_EveryForm_ReportsGsh0003AtDefault(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(SadPath, dialect);

        Assert.Equal(
            [
                (Analyzer, 13, 31, 13, 38),
                (Analyzer, 21, 17, 21, 45),
                (Analyzer, 30, 31, 30, 38),
                (Analyzer, 36, 56, 36, 63)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, Reason));
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
}
