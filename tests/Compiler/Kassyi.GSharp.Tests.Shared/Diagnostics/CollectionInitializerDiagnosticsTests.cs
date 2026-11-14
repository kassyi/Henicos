using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A collection initializer in GPU code whose <c>Add</c> G# cannot call reports what a written call of the same method
/// reports (GSH0107), at the element, instead of dropping the element (#1899); an instance method of a struct has no
/// device form yet (#451). An index initializer of a struct indexer keeps GSH0003: the indexer setter is a member G#
/// emits, but an element access of a struct indexer has no C++ form yet (#1033).
/// </summary>
[Category(TestCategory.Compiler)]
public class CollectionInitializerDiagnosticsTests
{
    private const string TestCase = "Diagnostics/CollectionInitializerUnsupported.cs";

    // Line 59: two elements of an instance Add; line 60: an element of two arguments; line 61: an extension Add without
    // [GpuDeviceFunction]; line 62: the collection initializer of a member
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnsupportedAdd_InitializerElement_ReportsGsh0107AtTheElement(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 59, 44, 59, 45),
                (Analyzer, 59, 47, 59, 48),
                (Analyzer, 60, 43, 60, 51),
                (Analyzer, 61, 46, 61, 47),
                (Analyzer, 62, 47, 62, 48)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id));
    }

    [Theory]
    [InlineData(59, "'InstanceBag.Add'", "instance methods of structs are not supported in GPU code yet")]
    [InlineData(61, "'UnmarkedBagExtensions.Add'", "add [GpuDeviceFunction]")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnsupportedAdd_DiagnosticMessage_NamesTheMethodCSharpCalls(
        int line,
        string member,
        string advice)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        var message = diagnostics
            .First(d => d.Id == GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id && d.StartLine == line)
            .Message;
        Assert.Contains(member, message);
        Assert.Contains(advice, message);
    }

    // An index initializer of a struct indexer (lines 62 and 63) or of an array member (line 64) is still a syntax the
    // transpiler does not write, and an Add that reaches a barrier (line 65) has no call form outside a written call
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task IndexInitializerOrBarrierAdd_InitializerElement_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 62, 62, 62, 65),
                (Analyzer, 63, 41, 63, 44),
                (Analyzer, 64, 57, 64, 60),
                (Analyzer, 65, 44, 65, 46)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }
}
