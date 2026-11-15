using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The parameters G# appends to a kernel's native signature, in the order the signature has them: the result pointer,
/// the grid aggregate scratch and the launch epoch, each aligned to its own size.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelHiddenParametersTests
{
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // CUDA, void, no grid aggregate: only the epoch
    [InlineData(false, false, true, 32760, 32764)]
    [InlineData(false, false, true, 32761, 32768)]
    // A CPU dialect appends the result pointer only
    [InlineData(true, false, false, 4, 16)]
    [InlineData(false, false, false, 5, 5)]
    // Result pointer, scratch pointer and epoch: 1 -> 8 + 8 = 16, + 8 = 24, + 4 = 28
    [InlineData(true, true, true, 1, 28)]
    // The scratch alone after an odd size: 3 -> 8 + 8 = 16, + 4 = 20
    [InlineData(false, true, true, 3, 20)]
    public void AppendTo_UserParameterBytes_AddsEachHiddenParameterAligned(
        bool resultBuffer,
        bool gridScratch,
        bool launchEpoch,
        int userBytes,
        int expected)
    {
        var hidden = new KernelHiddenParameters(resultBuffer, gridScratch, launchEpoch);

        Assert.Equal(expected, hidden.AppendTo(userBytes));
    }
}
