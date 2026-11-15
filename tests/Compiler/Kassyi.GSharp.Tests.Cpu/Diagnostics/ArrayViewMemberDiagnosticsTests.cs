using System.Globalization;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The methods of <c>Memory&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ArraySegment&lt;T&gt;</c> and
/// <c>ImmutableArray&lt;T&gt;</c> that the array view of device code defines with their C# meaning (Slice, AsSpan,
/// CopyTo, TryCopyTo) are not reported; the others are GSH0109: <c>ImmutableArray&lt;T&gt;.Clear()</c> returns an
/// empty array in C# where the array view's <c>Clear()</c> zeroes the elements, and the view has no
/// <c>CopyTo(T[], int)</c> or <c>ToArray()</c>.
/// </summary>
[Category(TestCategory.Compiler)]
public class ArrayViewMemberDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ArrayViewMembers.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task FaithfulMembers_SliceAsSpanAndCopies_ReportNothing(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        // FaithfulKernel ends before line 31, where UnfaithfulKernel starts
        Assert.Empty(run.Analyzer.Where(static d => d.Location.GetLineSpan().StartLinePosition.Line + 1 < 31));
        Assert.Empty(KernelDiagnostics.FindStatedFailures(run).Where(static f => StatedLine(f.Failure) < 31));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnfaithfulMembers_ClearAndMissingOverloads_ReportGsh0109AtCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 33, 13, 33, 24),
                (Analyzer, 34, 13, 34, 31),
                (Analyzer, 35, 13, 35, 31),
                (Analyzer, 36, 22, 36, 35)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id));
    }

    // The line of "(line,column): ID", a failure the generator states in its output
    private static int StatedLine(string failure) =>
        int.Parse(failure.AsSpan(1, failure.IndexOf(',') - 1), CultureInfo.InvariantCulture);
}
