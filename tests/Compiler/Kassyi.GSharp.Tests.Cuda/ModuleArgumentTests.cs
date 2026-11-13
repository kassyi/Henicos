#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// The arguments a module launcher passes to a hand-written kernel (<c>Native/module_arguments.cu</c>), through the
/// YAML binding <c>Native/module_arguments.yml</c> and through <c>[CudaImport]</c>
/// (<see cref="ModuleArgumentImports"/>): an array parameter is staged through a device buffer however its type is
/// spaced (<c>float[ ]</c>, <c>float [ ]</c>), null arrives as a null pointer for a nullable array and is rejected for
/// another, a <c>ref</c> or <c>out</c> value comes back, and a two-dimensional array is passed in row-major order.
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

    // slots[0]: the address the kernel received; slots[1]: set by the test to release the kernel; slots[2]: set by the
    // kernel when it stopped waiting before the release
    private const int SlotCount = 3;

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

        var (result, memoryType, address, timedOut) = LaunchAndQueryRecordedPointer(launch);

        Assert.False(timedOut, "The kernel timed out before the query, so the device buffer may have been freed.");
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

    // A YAML binding in the class of the [CudaImport] methods of the same module (Native/module_argument_imports.yml),
    // with parameters named by C# keywords: both kinds of binding launch from the module field they share
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_YamlAndImportInOneClass_BothLaunchFromTheSharedModule()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int n = 100;
        const uint blockSize = 64;
        var data = new float[n];
        for (var i = 0; i < n; i++)
        {
            data[i] = i - 7;
        }

        var total = 5;
        var seed = 4;

        ModuleArgumentImports.ScaleWithKeywordNames(data, n, 3f, (n + blockSize - 1) / blockSize, blockSize);
        ModuleArgumentImports.UpdateByReference(ref total, in seed, out var result);

        for (var i = 0; i < n; i++)
        {
            Assert.Equal(3f * (i - 7) + i, data[i]);
        }

        Assert.Equal(5 * 3 + 4, total);
        Assert.Equal(4 + 7, result);
    }

    // Null for an array declared nullable (float[]?) reaches the kernel as a null pointer instead of failing on its
    // length; the non-null control shows that the kernel ran and wrote the slot
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(Binding.Yaml)]
    [InlineData(Binding.Import)]
    public void Launch_NullForNullableArray_PassesNullPointer(Binding binding)
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        ulong[] seenNull = [ulong.MaxValue];
        ulong[] seenArray = [ulong.MaxValue];

        RecordAddress(binding, null, seenNull);
        RecordAddress(binding, new float[4], seenArray);

        Assert.Equal(0ul, seenNull[0]);
        Assert.NotEqual(0ul, seenArray[0]);
        Assert.NotEqual(ulong.MaxValue, seenArray[0]);
    }

    // Null for an array that is not declared nullable, one-dimensional or multidimensional, throws before anything is
    // copied or launched, as the launcher of a [GpuKernel] method does: the slot the kernel would write keeps its
    // value. The check precedes the CUDA context, so no device is needed.
    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(Binding.Yaml)]
    [InlineData(Binding.Import)]
    public void Launch_NullForNonNullableArray_ThrowsArgumentNullException(Binding binding)
    {
        // [WHY] Null is passed on purpose to parameters that are not nullable: the launcher must reject it
        float[] vector = null!;
        float[,] matrix = null!;
        ulong[] seen = [ulong.MaxValue];

        var vectorError = Assert.Throws<ArgumentNullException>(() =>
        {
            if (binding == Binding.Yaml)
            {
                ModuleArgumentKernels.RecordAddress(vector, seen);
            }
            else
            {
                ModuleArgumentImports.RecordAddress(vector, seen);
            }
        });
        var matrixError = Assert.Throws<ArgumentNullException>(() =>
        {
            if (binding == Binding.Yaml)
            {
                ModuleArgumentKernels.ShiftMatrix(matrix, 3, 5, 32);
            }
            else
            {
                ModuleArgumentImports.ShiftMatrix(matrix, 3, 5, 32);
            }
        });

        Assert.Equal("data", vectorError.ParamName);
        Assert.Equal("x", matrixError.ParamName);
        Assert.Equal(ulong.MaxValue, seen[0]);
    }

    // The kernel makes total = total * 3 + seed, result = <its value at the launch> + seed + 7, and overwrites seed on
    // the device: 'ref' is copied in and back, 'out' starts from default whatever the caller's variable held, and 'in'
    // (also 'ref readonly') is never copied back
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(Binding.Yaml)]
    [InlineData(Binding.Import)]
    [InlineData(Binding.ImportReadOnly)]
    [InlineData(Binding.ImportNested)]
    public void Launch_ValuesByReference_CopiesRefAndOutBack(Binding binding)
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var total = 5;
        var seed = 4;
        var result = 99;

        switch (binding)
        {
            case Binding.Yaml:
                ModuleArgumentKernels.UpdateByReference(ref total, in seed, out result);
                break;
            case Binding.Import:
                ModuleArgumentImports.UpdateByReference(ref total, in seed, out result);
                break;
            case Binding.ImportNested:
                ModuleArgumentNestedImports<long>.Keywords.Update(ref total, in seed, out result);
                break;
            default:
                ModuleArgumentImports.UpdateByReadOnlyReference(ref total, in seed, out result);
                break;
        }

        Assert.Equal(5 * 3 + 4, total);
        Assert.Equal(4, seed);
        Assert.Equal(4 + 7, result);
    }

    // The launcher keeps the default it sends for an 'out' value in a local of its own: the caller's variables change
    // only in the write-back after the launch. A launch the driver rejects (64 threads for a kernel bounded to 32)
    // throws after the arguments were staged and leaves them as they were; 32 threads run it as the control.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(Binding.Yaml)]
    [InlineData(Binding.Import)]
    public void Launch_RejectedAfterStaging_LeavesOutAndRefVariablesUnchanged(Binding binding)
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var total = 5;
        var seed = 4;
        var result = 99;

        Assert.Throws<CudaException>(() => UpdateBounded(binding, ref total, seed, out result, 64));

        Assert.Equal(5, total);
        Assert.Equal(99, result);

        UpdateBounded(binding, ref total, seed, out result, 32);

        Assert.Equal(5 * 3 + 4, total);
        Assert.Equal(4 + 7, result);
    }

    // Element [i, j] of a 3 x 5 matrix starts as 100 * i + j and the kernel adds i - j at index i * 5 + j: a
    // column-major or transposed copy would add j - i, or the values of other elements, so the result differs
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(Binding.Yaml)]
    [InlineData(Binding.Import)]
    public void Launch_TwoDimensionalArray_PassesRowMajorAndCopiesBack(Binding binding)
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int rows = 3;
        const int cols = 5;
        var matrix = new float[rows, cols];
        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < cols; j++)
            {
                matrix[i, j] = 100 * i + j;
            }
        }

        if (binding == Binding.Yaml)
        {
            ModuleArgumentKernels.ShiftMatrix(matrix, rows, cols, 32);
        }
        else
        {
            ModuleArgumentImports.ShiftMatrix(matrix, rows, cols, 32);
        }

        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < cols; j++)
            {
                Assert.Equal(100 * i + j + (i - j), matrix[i, j]);
            }
        }
    }

    public enum Binding
    {
        Yaml,
        Import,
        ImportReadOnly,

        // [CudaImport] in a class nested in a generic struct, with parameters named with keywords
        ImportNested,
    }

    private static void UpdateBounded(Binding binding, ref int total, int seed, out int result, uint blockDimX)
    {
        if (binding == Binding.Yaml)
        {
            ModuleArgumentKernels.UpdateByReferenceBounded(ref total, in seed, out result, blockDimX);
        }
        else
        {
            ModuleArgumentImports.UpdateByReferenceBounded(ref total, in seed, out result, blockDimX);
        }
    }

    private static void RecordAddress(Binding binding, float[]? data, ulong[] seen)
    {
        if (binding == Binding.Yaml)
        {
            ModuleArgumentKernels.RecordNullableAddress(data, seen);
        }
        else
        {
            ModuleArgumentImports.RecordNullableAddress(data, seen);
        }
    }

    // Launches on another thread, reads the address the kernel recorded while the kernel waits, asks the driver what
    // kind of memory it is, then releases the kernel
    private static unsafe (int Result, uint MemoryType, ulong Address, bool TimedOut) LaunchAndQueryRecordedPointer(
        Action<float[], ulong> launch)
    {
        IntPtr host;
        using (new GpuKernelLoader.CudaContextScope())
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemAllocHost(out host, SlotCount * sizeof(ulong)));
        }

        var slots = (ulong*)host;
        for (var i = 0; i < SlotCount; i++)
        {
            Volatile.Write(ref slots[i], 0ul);
        }

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

                    // Read after the query: a kernel that stopped waiting before it may have let the buffer go
                    return (result, memoryType, address, Volatile.Read(ref slots[2]) != 0);
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
