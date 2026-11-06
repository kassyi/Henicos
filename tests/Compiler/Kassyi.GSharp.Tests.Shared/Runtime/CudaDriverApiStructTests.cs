using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Unit tests validating the memory layout and existence of CUDA Driver API structures.
/// </summary>
public class CudaDriverApiStructTests
{
    // Sizes and offsets of CUDA_ARRAY_DESCRIPTOR, CUDA_RESOURCE_DESC, CUDA_TEXTURE_DESC and CUDA_RESOURCE_VIEW_DESC in
    // cuda.h on a 64-bit target. The driver reads the whole struct, reserved tails included, through the pointer.
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CudaDriverApi_ArrayDescriptor_MatchesCudaHeaderLayout()
    {
        var descriptor = default(CudaArrayDescriptor);

        Assert.Equal(24, Unsafe.SizeOf<CudaArrayDescriptor>());
        Assert.Equal(0, Offset(ref descriptor, ref descriptor.Width));
        Assert.Equal(8, Offset(ref descriptor, ref descriptor.Height));
        Assert.Equal(16, Offset(ref descriptor, ref descriptor.Format));
        Assert.Equal(20, Offset(ref descriptor, ref descriptor.NumChannels));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CudaDriverApi_ResourceDescriptor_MatchesCudaHeaderLayout()
    {
        var descriptor = default(CudaResourceDescriptor);

        Assert.Equal(144, Unsafe.SizeOf<CudaResourceDescriptor>());
        Assert.Equal(0, Offset(ref descriptor, ref descriptor.ResType));
        Assert.Equal(8, Offset(ref descriptor, ref descriptor.Res));
        Assert.Equal(136, Offset(ref descriptor, ref descriptor.Flags));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CudaDriverApi_TextureDescriptor_MatchesCudaHeaderLayout()
    {
        var descriptor = default(CudaTextureDescriptor);

        Assert.Equal(104, Unsafe.SizeOf<CudaTextureDescriptor>());
        Assert.Equal(0, Offset(ref descriptor, ref descriptor.AddressMode0));
        Assert.Equal(4, Offset(ref descriptor, ref descriptor.AddressMode1));
        Assert.Equal(8, Offset(ref descriptor, ref descriptor.AddressMode2));
        Assert.Equal(12, Offset(ref descriptor, ref descriptor.FilterMode));
        Assert.Equal(16, Offset(ref descriptor, ref descriptor.Flags));
        Assert.Equal(20, Offset(ref descriptor, ref descriptor.MaxAnisotropy));
        Assert.Equal(24, Offset(ref descriptor, ref descriptor.MipmapFilterMode));
        Assert.Equal(28, Offset(ref descriptor, ref descriptor.MipmapLevelBias));
        Assert.Equal(32, Offset(ref descriptor, ref descriptor.MinMipmapLevelClamp));
        Assert.Equal(36, Offset(ref descriptor, ref descriptor.MaxMipmapLevelClamp));
        Assert.Equal(40, Offset(ref descriptor, ref descriptor.BorderColor0));
        Assert.Equal(44, Offset(ref descriptor, ref descriptor.BorderColor1));
        Assert.Equal(48, Offset(ref descriptor, ref descriptor.BorderColor2));
        Assert.Equal(52, Offset(ref descriptor, ref descriptor.BorderColor3));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CudaDriverApi_ResourceViewDescriptor_MatchesCudaHeaderLayout()
    {
        var descriptor = default(CudaResourceViewDescriptor);

        Assert.Equal(112, Unsafe.SizeOf<CudaResourceViewDescriptor>());
        Assert.Equal(0, Offset(ref descriptor, ref descriptor.Format));
        Assert.Equal(8, Offset(ref descriptor, ref descriptor.Width));
        Assert.Equal(16, Offset(ref descriptor, ref descriptor.Height));
        Assert.Equal(24, Offset(ref descriptor, ref descriptor.Depth));
        Assert.Equal(32, Offset(ref descriptor, ref descriptor.FirstMipmapLevel));
        Assert.Equal(36, Offset(ref descriptor, ref descriptor.LastMipmapLevel));
        Assert.Equal(40, Offset(ref descriptor, ref descriptor.FirstLayer));
        Assert.Equal(44, Offset(ref descriptor, ref descriptor.LastLayer));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void Test_CudaDriverApi_ResourceDescriptor_LeavesUnionTailAndPaddingZero()
    {
        var descriptor = new CudaResourceDescriptor { ResType = -1, Res = new IntPtr(-1), Flags = uint.MaxValue };
        var bytes = new ReadOnlySpan<byte>(&descriptor, sizeof(CudaResourceDescriptor));

        Assert.True(bytes[4..8].IndexOfAnyExcept((byte)0) < 0);
        Assert.True(bytes[16..136].IndexOfAnyExcept((byte)0) < 0);
        Assert.True(bytes[140..].IndexOfAnyExcept((byte)0) < 0);
    }

    private static long Offset<TStruct, TField>(ref TStruct owner, ref TField field) =>
        Unsafe.ByteOffset(ref Unsafe.As<TStruct, byte>(ref owner), ref Unsafe.As<TField, byte>(ref field));

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
