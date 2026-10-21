#nullable enable
using System.Threading.Tasks;
using Kassyi.GSharp.Tests.Shared.Helpers;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public class GpuMacroTranspilerRuleTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_GpuMacroTranspilerRule_SadPath_ReportsDiagnostic()
    {
        await VerifyGeneratorAsync();
    }
}
