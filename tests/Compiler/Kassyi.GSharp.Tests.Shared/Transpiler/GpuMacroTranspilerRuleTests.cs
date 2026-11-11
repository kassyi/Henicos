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
    public async Task GpuMacroTranspilerRule_UnsupportedMacroUsage_ReportsDiagnostic()
    {
        await VerifyGeneratorAsync();
    }
}
