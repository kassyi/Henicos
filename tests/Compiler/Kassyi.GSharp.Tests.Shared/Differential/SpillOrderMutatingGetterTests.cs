#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

[Collection(TestCollections.GlobalStateSequential)]
public sealed class SpillOrderMutatingGetterTests : TranspilerTestBase
{
    private const int ThreadCount = 32;

    private static Assembly CompileManagedShapeAssembly(string testId, string relativePath = "Transpiler/Emission/SpillOrderMutatingGetterShapeKernels.cu.cs")
    {
        string source = RoslynTestUtility.ReadTestCase(relativePath);
        var (_, outputCompilation, generatorDiagnostics) = RoslynTestUtility.RunGenerator(source, "Cpu");
        var genErrors = generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(genErrors);

        using var peStream = new MemoryStream();
        var emitResult = outputCompilation.Emit(peStream);
        if (!emitResult.Success)
        {
            string errors = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            Assert.Fail($"Managed compilation failed:{Environment.NewLine}{errors}");
        }

        peStream.Position = 0;
        var loadContext = new AssemblyLoadContext($"SpillOrderMutatingGetter_{testId}_{Guid.NewGuid():N}", isCollectible: true);
        return loadContext.LoadFromStream(peStream);
    }

    private static Action<int[], int> CompileAndCreateManagedSeedInvoker(
        string runMethodName,
        string relativePath = "Transpiler/Emission/SpillOrderMutatingGetterShapeKernels.cu.cs",
        string typeName = "Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.Emission.SpillOrderMutatingGetterShapeKernels")
    {
        var assembly = CompileManagedShapeAssembly(runMethodName, relativePath);
        var kernelsType = assembly.GetType(typeName, throwOnError: true);
        Assert.NotNull(kernelsType);
        var runMethod = kernelsType.GetMethod(runMethodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(runMethod);

        return (output, seed) =>
        {
            object?[] parameters = [output, seed, (uint)ThreadCount];
            runMethod.Invoke(null, parameters);
        };
    }

    private static unsafe Action<int[], int[]> CompileAndCreateManagedPointerInvoker(string runMethodName)
    {
        var assembly = CompileManagedShapeAssembly(runMethodName, "Transpiler/Emission/SpillOrderPointerShapeKernels.cu.cs");
        var kernelsType = assembly.GetType("Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.Emission.SpillOrderPointerShapeKernels", throwOnError: true);
        Assert.NotNull(kernelsType);
        var runMethod = kernelsType.GetMethod(runMethodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(runMethod);

        return (backing, output) =>
        {
            fixed (int* basePtr = backing)
            {
                object?[] parameters = [(nint)basePtr, output, (uint)ThreadCount];
                runMethod.Invoke(null, parameters);
            }
        };
    }

    // -------------------------------------------------------------------------
    // Shape 1 (#992): Mutating property getter on kernel value parameter across barrier
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MutatingGetterOnKernelValueParameterAcrossBarrier_PreservesReceiverWriteAfterBarrier()
    {
        Skip.If(IsCudaDialect, "Managed compilation shape test runs on the CPU runner.");

        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingGetterOnKernelValueParameterAcrossBarrier");
        int[] actual = new int[ThreadCount];
#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            invoke(actual, 10);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif

        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int counterVal = 10 + 1;
            int first = counterVal + tid;
            expected[tid] = first * 100 + counterVal;
        }

        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // Shape 2 (#992): Mutating property getter on suspending device function value parameter across barrier
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MutatingGetterOnDeviceValueParameterAcrossBarrier_PreservesReceiverWriteAfterBarrier()
    {
        Skip.If(IsCudaDialect, "Managed compilation shape test runs on the CPU runner.");

        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingGetterOnDeviceValueParameterAcrossBarrier");
        int[] actual = new int[ThreadCount];
#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            invoke(actual, 20);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif

        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int counterVal = 20 + 1;
            int before = counterVal + tid;
            expected[tid] = before * 100 + counterVal;
        }

        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // Shape 3 (#992): Mutating indexer getter on kernel value parameter across barrier
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MutatingIndexerOnKernelValueParameterAcrossBarrier_PreservesReceiverWriteAfterBarrier()
    {
        Skip.If(IsCudaDialect, "Managed compilation shape test runs on the CPU runner.");

        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingIndexerOnKernelValueParameterAcrossBarrier");
        int[] actual = new int[ThreadCount];
#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            invoke(actual, 5);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif

        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int step = tid + 2;
            int counterVal = 5 + step;
            expected[tid] = counterVal * 100 + counterVal;
        }

        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // Shape 4 (#992 / F10): Mutating property getter after reading struct field in call arguments
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MutatingGetterAfterStructIdentifierArgument_SpillsEarlierReadBeforeGetterMutates()
    {
        Skip.If(IsCudaDialect, "Managed compilation shape test runs on the CPU runner.");

        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingGetterAfterStructIdentifierArgument");
        int[] actual = new int[ThreadCount];
#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            invoke(actual, 10);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif

        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int initialVal = 10 + tid;
            int afterNext = initialVal + 1;
            int combined = initialVal * 100 + afterNext;
            expected[tid] = combined * 100 + afterNext;
        }

        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // Shape 5 (#992 / F10): Mutating indexer getter after reading struct field in call arguments
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MutatingIndexerAfterStructIdentifierArgument_SpillsEarlierReadBeforeIndexerMutates()
    {
        Skip.If(IsCudaDialect, "Managed compilation shape test runs on the CPU runner.");

        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingIndexerAfterStructIdentifierArgument");
        int[] actual = new int[ThreadCount];
#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            invoke(actual, 10);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif

        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int initialVal = 10 + tid;
            int afterIndexer = initialVal + 3;
            int combined = initialVal * 100 + afterIndexer;
            expected[tid] = combined * 100 + afterIndexer;
        }

        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // Shape 6 (#1000): Conditional ref write on kernel value parameters across barrier
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefConditionalWriteOnKernelValueParameterAcrossBarrier_MatchesExpected()
    {
        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int l = 10;
            int r = 20;
            bool pickLeft = (tid & 1) == 0;
            (pickLeft ? ref l : ref r) += tid + 5;
            expected[tid] = l * 100 + r;
        }

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchRefConditionalWriteOnKernelValueParameterAcrossBarrierKernel(
            actual, 10, 20, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

#if GSHARP_CPU
        int[] managedActual = new int[ThreadCount];
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            SpillOrderMutatingGetterKernels.LaunchRefConditionalWriteOnKernelValueParameterAcrossBarrierKernel(
                managedActual, 10, 20, blockDimX: ThreadCount);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }

        Assert.Equal(expected, managedActual);
#endif
    }

    // -------------------------------------------------------------------------
    // Shape 7 (#1000): Conditional ref argument on kernel value parameters across barrier
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefConditionalArgumentOnKernelValueParameterAcrossBarrier_MatchesExpected()
    {
        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int l = 10;
            int r = 20;
            bool pickLeft = (tid & 1) == 0;
            (pickLeft ? ref l : ref r) += tid + 7;
            expected[tid] = l * 100 + r;
        }

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchRefConditionalArgumentOnKernelValueParameterAcrossBarrierKernel(
            actual, 10, 20, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

#if GSHARP_CPU
        int[] managedActual = new int[ThreadCount];
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            SpillOrderMutatingGetterKernels.LaunchRefConditionalArgumentOnKernelValueParameterAcrossBarrierKernel(
                managedActual, 10, 20, blockDimX: ThreadCount);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }

        Assert.Equal(expected, managedActual);
#endif
    }

    // -------------------------------------------------------------------------
    // Shape 8 (#1000): Conditional ref write on suspending device function value parameters across barrier
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefConditionalWriteOnDeviceValueParameterAcrossBarrier_MatchesExpected()
    {
        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int l = 15;
            int r = 25;
            int selector = tid & 1;
            (selector == 0 ? ref l : ref r) += tid + 3;
            expected[tid] = l * 100 + r;
        }

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchRefConditionalWriteOnDeviceValueParameterAcrossBarrierKernel(
            actual, 15, 25, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

#if GSHARP_CPU
        int[] managedActual = new int[ThreadCount];
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            SpillOrderMutatingGetterKernels.LaunchRefConditionalWriteOnDeviceValueParameterAcrossBarrierKernel(
                managedActual, 15, 25, blockDimX: ThreadCount);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }

        Assert.Equal(expected, managedActual);
#endif
    }

    // -------------------------------------------------------------------------
    // #944: [GpuOperatorMacro] implicitly passing kernel value parameter by ref across barrier
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperatorMacroOnKernelValueParameterAcrossBarrier_PreservesContextMutations()
    {
        var initialState = new Kassyi.GSharp.Tests.Shared.Syntax.OperatorMacroContextState { Offset = 100, Calls = 0 };
        uint[] result = [0];
        uint[] calls = [0];

        SpillOrderMutatingGetterKernels.LaunchOperatorMacroOnKernelValueParameterAcrossBarrierKernel(
            initialState, [3], [4], result, calls);
        Assert.Equal(107u, result[0]);
        Assert.Equal(1u, calls[0]);

#if GSHARP_CPU
        uint[] managedResult = [0];
        uint[] managedCalls = [0];
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            SpillOrderMutatingGetterKernels.LaunchOperatorMacroOnKernelValueParameterAcrossBarrierKernel(
                initialState, [3], [4], managedResult, managedCalls);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }

        Assert.Equal(107u, managedResult[0]);
        Assert.Equal(1u, managedCalls[0]);
#endif
    }

    // -------------------------------------------------------------------------
    // #944: [GpuOperatorMacro] implicitly passing suspending device function value parameter by ref across barrier
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperatorMacroOnDeviceValueParameterAcrossBarrier_PreservesContextMutations()
    {
        var initialState = new Kassyi.GSharp.Tests.Shared.Syntax.OperatorMacroContextState { Offset = 50, Calls = 0 };
        uint[] result = [0];
        uint[] calls = [0];

        SpillOrderMutatingGetterKernels.LaunchOperatorMacroOnDeviceValueParameterAcrossBarrierKernel(
            initialState, [5], [6], result, calls);
        Assert.Equal(61u, result[0]);
        Assert.Equal(1u, calls[0]);

#if GSHARP_CPU
        uint[] managedResult = [0];
        uint[] managedCalls = [0];
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            SpillOrderMutatingGetterKernels.LaunchOperatorMacroOnDeviceValueParameterAcrossBarrierKernel(
                initialState, [5], [6], managedResult, managedCalls);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }

        Assert.Equal(61u, managedResult[0]);
        Assert.Equal(1u, managedCalls[0]);
#endif
    }

    // -------------------------------------------------------------------------
    // #958 (CS0214): External pointer stored in local pointer across barrier on Managed state machine
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_PointerLocalAcrossBarrier_CompilesWithUnsafeContextClassAndMatchesExpected()
    {
        Skip.If(IsCudaDialect, "Managed compilation shape test runs on the CPU runner.");

        var invoke = CompileAndCreateManagedPointerInvoker("RunPointerLocalAcrossBarrier");
        int[] backing = Enumerable.Range(100, ThreadCount).ToArray();
        int[] actual = new int[ThreadCount];
#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            invoke(backing, actual);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif

        int[] expected = Enumerable.Range(100, ThreadCount).Select(v => v + 10).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // #958 (CS0212): Taking address of copied value parameter before barrier on Managed state machine
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_AddressOfValueParameterBeforeBarrier_PinsContextFieldAndMatchesExpected()
    {
        Skip.If(IsCudaDialect, "Managed compilation shape test runs on the CPU runner.");

        var invoke = CompileAndCreateManagedSeedInvoker(
            "RunAddressOfValueParameterBeforeBarrier",
            "Transpiler/Emission/SpillOrderPointerShapeKernels.cu.cs",
            "Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.Emission.SpillOrderPointerShapeKernels");
        int[] actual = new int[ThreadCount];
#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            invoke(actual, 40);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif

        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => 40 + tid + 5 + 3).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // #958 / N2 (CS0212): Taking address of hoisted local before barrier on Managed state machine
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_AddressOfHoistedLocalBeforeBarrier_PinsContextFieldAndMatchesExpected()
    {
        Skip.If(IsCudaDialect, "Managed compilation shape test runs on the CPU runner.");

        var invoke = CompileAndCreateManagedSeedInvoker(
            "RunAddressOfHoistedLocalBeforeBarrier",
            "Transpiler/Emission/SpillOrderPointerShapeKernels.cu.cs",
            "Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.Emission.SpillOrderPointerShapeKernels");
        int[] actual = new int[ThreadCount];
#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            invoke(actual, 50);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif

        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => 50 + tid + 7).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // F14: unsafe { ... Gpu.SyncThreads(); ... } flattened by BranchRewriter
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_UnsafeBlockWithSyncThreads_SplitsStateMachineCorrectly()
    {
        int[] expected = new int[ThreadCount];
        for (int tid = 0; tid < ThreadCount; tid++)
        {
            int peer = (tid + 1) & 31;
            expected[tid] = peer + 10;
        }

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchUnsafeBlockWithSyncThreadsKernel(
            actual, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

#if GSHARP_CPU
        int[] managedActual = new int[ThreadCount];
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            SpillOrderMutatingGetterKernels.LaunchUnsafeBlockWithSyncThreadsKernel(
                managedActual, blockDimX: ThreadCount);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }

        Assert.Equal(expected, managedActual);
#endif
    }

    // -------------------------------------------------------------------------
    // F14: unsafe { ... Warp.ShuffleSync(...); ... } flattened by BranchRewriter
    // -------------------------------------------------------------------------
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_UnsafeBlockWithWarpShuffle_SplitsStateMachineCorrectly()
    {
        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => 100 + tid).ToArray();

        int[] actual = new int[ThreadCount];
        SpillOrderMutatingGetterKernels.LaunchUnsafeBlockWithWarpShuffleKernel(
            actual, 100, blockDimX: ThreadCount);
        Assert.Equal(expected, actual);

#if GSHARP_CPU
        int[] managedActual = new int[ThreadCount];
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            SpillOrderMutatingGetterKernels.LaunchUnsafeBlockWithWarpShuffleKernel(
                managedActual, 100, blockDimX: ThreadCount);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }

        Assert.Equal(expected, managedActual);
#endif
    }
}
