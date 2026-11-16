using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Generator;

public partial class GeneratorSnapshotTests
{
    // Mutation testing (just mutate compiler) runs only Kassyi.GSharp.Tests.Cpu; these test cases compare their CUDA
    // output with the _Cuda snapshots from there too, so that the CUDA-only emission (the dynamic shared memory table,
    // the template header and the table parameter of the device functions that reach it) is checked by that run
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("tests/Compiler/Kassyi.GSharp.Tests.Shared/Core/DeviceDynamicSharedMemoryKernels.cu.cs")]
    [InlineData("tests/Compiler/Kassyi.GSharp.Tests.Shared/Core/SharedMemoryRegionKernels.cu.cs")]
    [InlineData("tests/Compiler/Kassyi.GSharp.Tests.Shared/Syntax/ConcurrencyAndWarpKernels.cu.cs")]
    public Task GeneratorSnapshots_CudaDialectFromCpuTests_MatchesVerifiedSnapshots(string relativeFilePath) =>
        VerifyTestCaseSnapshotAsync(relativeFilePath, "Cuda");
}
