namespace Kassyi.GSharp.Tests;

using Xunit;
using System.Threading.Tasks;

public class GEN_08_KernelReturnTypesTests : Helpers.TranspilerTestBase
{
    [SkippableFact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "SadPath")]
    public async Task Test_ManagedKernelReturns_ReportsDiagnostics()
    {
        var driver = await TestGenerator("Generator/GEN-08_KernelReturnTypes_SadPath.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }
}
