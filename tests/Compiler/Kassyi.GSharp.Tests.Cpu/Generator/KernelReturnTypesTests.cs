namespace Kassyi.GSharp.Tests.Shared.Generator;

using Xunit;
using System.Threading.Tasks;

public class KernelReturnTypesTests : Helpers.TranspilerTestBase
{
    // The analyzer reports GSH0004 (KernelSignatureDiagnosticsTests); this pins what the generator emits for the kernels
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ManagedKernelReturns_NonVoidReturnType_GeneratesExpectedSource()
    {
        var driver = await TestGenerator("Generator/KernelReturnTypes_SadPath.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }
}
