using System.Diagnostics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// The arguments a YAML module launcher (<c>Native/module_arguments.yml</c>) passes to a hand-written kernel: an array
/// parameter is staged through a device buffer however its type is spaced (<c>float[ ]</c>, <c>float [ ]</c>).
/// </summary>
/// <remarks>
/// <c>RecordPointer</c> writes the address it received to page-locked host memory and waits until the test has asked
/// the driver about it, so the device buffer behind the address is still allocated; after the launcher returns it is
/// freed, and the driver then answers for a freed device address as for a host address. The kernel never reads through
/// the address, so a launcher that passes a host address fails the test instead of faulting the device.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
public class ModuleArgumentTests
{
    private const int PointerAttributeMemoryType = 2;
    private const uint MemoryTypeDevice = 2;
    private const int CudaSuccess = 0;

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("float[ ]")]
    [InlineData("float [ ]")]
    public unsafe void Launch_ArrayTypeWrittenWithSpaces_PassesDeviceMemory(string spelling)
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        Action<float[], ulong> launch = spelling switch
        {
            "float[ ]" => ModuleArgumentKernels.RecordPointerSpaceInRank,
            _ => ModuleArgumentKernels.RecordPointerSpaceBeforeRank,
        };

        var (result, memoryType, address) = LaunchAndQueryRecordedPointer(launch);

        Assert.NotEqual(0ul, address);
        Assert.True(result == CudaSuccess,
            $"cuPointerGetAttribute(0x{address:x}) returned {result}: the kernel did not receive a device address.");
        Assert.Equal(MemoryTypeDevice, memoryType);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_ArrayTypeWrittenWithSpaces_CopiesValuesInAndBack()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int n = 300;
        const uint blockSize = 128;
        var data = new float[n + 1];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = 3 * i + 1;
        }

        ModuleArgumentKernels.Scale(data, n, 2f, (n + blockSize - 1) / blockSize, blockSize);

        for (var i = 0; i < n; i++)
        {
            Assert.Equal(2f * (3 * i + 1) + i, data[i]);
        }

        // The element past n is copied back as it was
        Assert.Equal(3f * n + 1, data[n]);
    }

    // Launches on another thread, reads the address the kernel recorded while the kernel waits, asks the driver what
    // kind of memory it is, then releases the kernel
    private static unsafe (int Result, uint MemoryType, ulong Address) LaunchAndQueryRecordedPointer(
        Action<float[], ulong> launch)
    {
        IntPtr host;
        using (new GpuKernelLoader.CudaContextScope())
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemAllocHost(out host, 2 * sizeof(ulong)));
        }

        var slots = (ulong*)host;
        Volatile.Write(ref slots[0], 0ul);
        Volatile.Write(ref slots[1], 0ul);
        try
        {
            IntPtr deviceSlots;
            using (new GpuKernelLoader.CudaContextScope())
            {
                CudaException.ThrowIfError(CudaDriverApi.cuMemHostGetDevicePointer(out deviceSlots, host, 0));
            }

            var launched = Task.Run(() => launch(new float[16], (ulong)deviceSlots));
            try
            {
                var address = WaitForRecordedAddress(slots, launched);
                using (new GpuKernelLoader.CudaContextScope())
                {
                    var result = GetMemoryType(address, out var memoryType);
                    return (result, memoryType, address);
                }
            }
            finally
            {
                Volatile.Write(ref slots[1], 1ul);
                Assert.True(launched.Wait(TimeSpan.FromSeconds(30)), "The launcher did not return.");
            }
        }
        finally
        {
            using (new GpuKernelLoader.CudaContextScope())
            {
                CudaException.ThrowIfError(CudaDriverApi.cuMemFreeHost(host));
            }
        }
    }

    private static unsafe ulong WaitForRecordedAddress(ulong* slots, Task launched)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            var address = Volatile.Read(ref slots[0]);
            if (address != 0)
            {
                return address;
            }

            if (launched.IsCompleted)
            {
                // Throws the launcher's exception, or fails when the kernel returned without recording
                launched.GetAwaiter().GetResult();
                return Volatile.Read(ref slots[0]);
            }

            Thread.SpinWait(64);
        }

        throw new TimeoutException("The kernel did not record the address it received.");
    }

    // cuPointerGetAttribute(CU_POINTER_ATTRIBUTE_MEMORY_TYPE), which Kassyi.GSharp.Runtime does not bind
    private static unsafe int GetMemoryType(ulong address, out uint memoryType)
    {
        var library = NativeLibrary.Load(OperatingSystem.IsWindows() ? "nvcuda.dll" : "libcuda.so.1");
        var function = (delegate* unmanaged<uint*, int, ulong, int>)NativeLibrary.GetExport(library,
            "cuPointerGetAttribute");
        uint value = 0;
        var result = function(&value, PointerAttributeMemoryType, address);
        memoryType = value;
        return result;
    }
}
