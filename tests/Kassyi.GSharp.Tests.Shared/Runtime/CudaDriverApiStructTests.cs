using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Unit tests validating the memory layout and existence of CUDA Driver API structures.
/// </summary>
public class CudaDriverApiStructTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CudaDriverApi_DescriptorStructs_HaveValidSizes()
    {
        // Happy Path: All newly defined Phase 4 descriptor structs must have non-zero unmanaged sizes
        Assert.True(Unsafe.SizeOf<CudaArrayDescriptor>() > 0);
        Assert.True(Unsafe.SizeOf<CudaResourceDescriptor>() > 0);
        Assert.True(Unsafe.SizeOf<CudaTextureDescriptor>() > 0);
        Assert.True(Unsafe.SizeOf<CudaResourceViewDescriptor>() > 0);
    }
}
