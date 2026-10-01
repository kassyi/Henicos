namespace Kassyi.GSharp.Tests.Shared.Generator;

using Xunit;
using System.Threading.Tasks;

public class KernelReturnTypesTests : Helpers.TranspilerTestBase
{
    [Fact]
    [Trait("Category", "SadPath")]
    public async Task Test_ManagedKernelReturns_ReportsDiagnostics()
    {
        var driver = await TestGenerator("Generator/KernelReturnTypes_SadPath.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }
}
