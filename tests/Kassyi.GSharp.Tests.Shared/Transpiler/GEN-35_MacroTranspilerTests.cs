#nullable enable
using System.Threading.Tasks;
using Kassyi.GSharp.Tests.Helpers;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Transpiler;

public class GEN_35_MacroTranspilerTests : TranspilerTestBase
{
    [SkippableFact]
    [Trait("Category", "Transpiler")]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "GEN-35")]
    public async Task Test_GEN35_MacroTranspiler_ExpandsMacrosCorrectly_Snapshots()
    {
        var config = TestGenerator("Transpiler/GEN-35_MacroTranspiler_InGpuKernel.cu.cs");
        var driver = await config.GetDriverAsync();

        await Verify(driver);
    }
}
