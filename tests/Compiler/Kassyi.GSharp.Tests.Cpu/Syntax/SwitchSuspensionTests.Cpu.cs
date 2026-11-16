using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SwitchSuspensionTests
{
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(float.NaN)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void SwitchSuspension_NaNCase_MatchesNaN(float selector) =>
        SwitchSuspensionScenarios.NaNCase(s_runNative, selector);
}
