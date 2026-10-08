using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class InvocationTests
:  TranspilerTestBase{
    // SYN-49 Happy Path
    // SYN-49 Happy Path
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_InterlockedAtomicOperations()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        int[] intBuf = new int[3];
        uint[] uintBuf = new uint[3];
        long[] longBuf = new long[3];
        ulong[] ulongBuf = new ulong[3];

        const int numThreads = 64;
        InvocationAndIntrinsicsKernels.LaunchInterlockedKernel(
            intBuf, uintBuf, longBuf, ulongBuf, blockDimX: numThreads);

        Assert.Equal(numThreads, intBuf[0]);
        Assert.Equal(numThreads * 2L, longBuf[0]);

        Assert.Equal(100, intBuf[1]);
        Assert.Equal(200u, uintBuf[1]);
        Assert.Equal(300L, longBuf[1]);
        Assert.Equal(400ul, ulongBuf[1]);

        Assert.Equal(500, intBuf[2]);
        Assert.Equal(600u, uintBuf[2]);
        Assert.Equal(700L, longBuf[2]);
        Assert.Equal(800ul, ulongBuf[2]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_InterlockedAtomicOperations_EmptyArray()
    {
        int[] intBuf = [];
        uint[] uintBuf = [];
        long[] longBuf = [];
        ulong[] ulongBuf = [];

        var ex = Record.Exception(() => 
            InvocationAndIntrinsicsKernels.LaunchInterlockedKernel(
                intBuf, uintBuf, longBuf, ulongBuf, blockDimX: 0));

        Assert.Null(ex);
    }

    // SYN-49 Edge Cases

    // SYN-49 Edge Cases
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_InterlockedEdgeCases_ArrayElementsAndStructFields()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        int[] array = new int[4] { 1, 2, 3, 4 };
        AtomicTestStruct[] structArray = new AtomicTestStruct[4]
        {
            new AtomicTestStruct { ValueI = 10, ValueU = 0, ValueL = 0 },
            new AtomicTestStruct { ValueI = 20, ValueU = 0, ValueL = 0 },
            new AtomicTestStruct { ValueI = 30, ValueU = 0, ValueL = 0 },
            new AtomicTestStruct { ValueI = 40, ValueU = 0, ValueL = 0 },
        };

        InvocationAndIntrinsicsKernels.LaunchInterlockedEdgeCasesKernel(
            array, structArray, blockDimX: 4);

        Assert.True(array.AsStackEnumerable().Select((val, i) =>
            val == i + 1 + 10 &&
            structArray[i].ValueI == (i + 1) * 10 + 1 &&
            structArray[i].ValueU == 42u &&
            structArray[i].ValueL == 99L).All(ok => ok));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_InterlockedEdgeCases_EmptyArray()
    {
        int[] emptyArray = [];
        AtomicTestStruct[] emptyStructArray = [];

        // Should be rejected or safely handled (we assert no exception is thrown when blockDimX is 0, or we catch exception)
        var ex = Record.Exception(() => 
            InvocationAndIntrinsicsKernels.LaunchInterlockedEdgeCasesKernel(
                emptyArray, emptyStructArray, blockDimX: 0));

        Assert.Null(ex);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    public void Test_AtomicTestStruct_MemoryLayout()
    {
        Assert.Equal(16, System.Runtime.InteropServices.Marshal.SizeOf<AtomicTestStruct>());
        Assert.Equal(0, System.Runtime.InteropServices.Marshal.OffsetOf<AtomicTestStruct>("ValueI").ToInt32());
        Assert.Equal(4, System.Runtime.InteropServices.Marshal.OffsetOf<AtomicTestStruct>("ValueU").ToInt32());
        Assert.Equal(8, System.Runtime.InteropServices.Marshal.OffsetOf<AtomicTestStruct>("ValueL").ToInt32());
    }
}
