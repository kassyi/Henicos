using Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// Forms whose C# meaning is a call or an object that the source does not write: a lambda converted to an expression
/// tree (objects on the heap, GSH0003), and the implicit calls of a <c>[CollectionBuilder]</c> collection expression
/// and of a <c>fixed</c> statement over <c>GetPinnableReference</c>, decided as a written call (GSH0107, GSH0109) or
/// reported when the callee needs what the analyses do not see through them (GSH0003). Each is reported once, at the
/// form.
/// </summary>
[Category(TestCategory.Compiler)]
public class ImplicitCallDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ImplicitCallForms.cu.cs";

    // Lines 21 to 23 convert a lambda to Expression<Func<int>>, Expression<Func<int, int>> and LambdaExpression; the
    // Func<int> of line 24 is a delegate
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ExpressionTreeLambda_InKernel_ReportsGsh0003AtLambda(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 21, 38, 21, 51),
                (Analyzer, 22, 42, 22, 52),
                (Analyzer, 23, 36, 23, 43)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, GpuCodeConstraints.ExpressionTreeLambda));
    }

    // Line 32 builds UnmarkedBag with a builder that is not a device function; line 57 pins through an instance method
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ImplicitCallOfUnmarkedMethod_BuilderOrPinnableReference_ReportsGsh0107AtForm(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 32, 27, 32, 33),
                (Analyzer, 57, 25, 57, 31)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id));
        Assert.Equal(
            [(Analyzer, 57, 25, 57, 31)],
            diagnostics.Ranges(GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode.Id, "instance methods of structs"));
    }

    // ImmutableArray<int> is built by ImmutableArray.Create, which allocates its array on the heap
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task BclCollectionBuilder_ImmutableArray_ReportsGsh0109AtCollectionExpression(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 40, 38, 40, 44)],
            diagnostics.Ranges(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id, "ImmutableArray.Create"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task CollectionBuilderReachingBarrier_DeviceFunction_ReportsGsh0003AtCollectionExpression(
        string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 48, 26, 48, 32)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, ImplicitDeviceCall.SuspendingMethod));
    }
}
