using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Unit tests validating the memory layout and existence of CUDA Driver API structures.
/// </summary>
public class CudaDriverApiStructTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CudaDriverApi_DescriptorStructs_HaveValidSizes()
    {
        // Happy Path: All newly defined Phase 4 descriptor structs must have non-zero unmanaged sizes
        Assert.True(Unsafe.SizeOf<CudaArrayDescriptor>() > 0);
        Assert.True(Unsafe.SizeOf<CudaResourceDescriptor>() > 0);
        Assert.True(Unsafe.SizeOf<CudaTextureDescriptor>() > 0);
        Assert.True(Unsafe.SizeOf<CudaResourceViewDescriptor>() > 0);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CUresult_ValuesAndNames_MatchCudaDriverApiHeader()
    {
        // Verify every CUresult member matches its exact value in cuda.h (cudaError_enum)
        var expected = new (string Name, int Value)[]
        {
            ("CudaSuccess", 0),
            ("CudaErrorInvalidValue", 1),
            ("CudaErrorOutOfMemory", 2),
            ("CudaErrorNotInitialized", 3),
            ("CudaErrorDeinitialized", 4),
            ("CudaErrorNoDevice", 100),
            ("CudaErrorInvalidDevice", 101),
            ("CudaErrorInvalidImage", 200),
            ("CudaErrorInvalidContext", 201),
            ("CudaErrorContextAlreadyCurrent", 202),
            ("CudaErrorMapFailed", 205),
            ("CudaErrorUnmapFailed", 206),
            ("CudaErrorArrayIsMapped", 207),
            ("CudaErrorAlreadyMapped", 208),
            ("CudaErrorNoBinaryForGpu", 209),
            ("CudaErrorAlreadyAcquired", 210),
            ("CudaErrorNotMapped", 211),
            ("CudaErrorNotMappedAsArray", 212),
            ("CudaErrorNotMappedAsPointer", 213),
            ("CudaErrorEccUncorrectable", 214),
            ("CudaErrorUnsupportedLimit", 215),
            ("CudaErrorContextAlreadyInUse", 216),
            ("CudaErrorUnsupportedPtxVersion", 222),
            ("CudaErrorInvalidSource", 300),
            ("CudaErrorFileNotFound", 301),
            ("CudaErrorSharedObjectSymbolNotFound", 302),
            ("CudaErrorSharedObjectInitFailed", 303),
            ("CudaErrorOperatingSystem", 304),
            ("CudaErrorInvalidHandle", 400),
            ("CudaErrorNotFound", 500),
            ("CudaErrorNotReady", 600),
            ("CudaErrorIllegalAddress", 700),
            ("CudaErrorLaunchOutOfResources", 701),
            ("CudaErrorLaunchTimeout", 702),
            ("CudaErrorLaunchIncompatibleTexturing", 703),
            ("CudaErrorAssert", 710),
            ("CudaErrorIllegalInstruction", 715),
            ("CudaErrorLaunchFailed", 719),
            ("CudaErrorNotSupported", 801),
            ("CudaErrorStreamCaptureUnsupported", 900),
            ("CudaErrorStreamCaptureInvalidated", 901),
            ("CudaErrorStreamCaptureMerge", 902),
            ("CudaErrorStreamCaptureUnmatched", 903),
            ("CudaErrorStreamCaptureUnjoined", 904),
            ("CudaErrorStreamCaptureIsolation", 905),
            ("CudaErrorStreamCaptureImplicit", 906),
            ("CudaErrorCapturedEvent", 907),
            ("CudaErrorUnknown", 999)
        };

        var actualNames = Enum.GetNames<CUresult>();
        Assert.Equal(expected.Select(e => e.Name), actualNames);

        foreach (var (name, value) in expected)
        {
            Assert.True(Enum.TryParse<CUresult>(name, out var parsed), $"Missing CUresult.{name}");
            Assert.Equal(value, (int)parsed);
            Assert.Equal(name, ((CUresult)value).ToString());
            Assert.Equal($"CUDA Error: {name}", new CudaException((CUresult)value).Message);
        }
    }
}
