#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// In-memory generator and managed state-machine compilation shape tests for mutating getters, indexers, pointers, and
/// ref/address-of locals across barriers (#957, #958, #966, #970, #992, #1036).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class SpillOrderMutatingGetterTests : TranspilerTestBase
{
    private const int ThreadCount = 32;

    private const string PointerShapePath = "Transpiler/Emission/SpillOrderPointerShapeKernels.cu.cs";

    private const string PointerShapeTypeName =
        "Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.Emission.SpillOrderPointerShapeKernels";

    private static Assembly CompileManagedShapeAssembly(
        string testId,
        string relativePath = "Transpiler/Emission/SpillOrderMutatingGetterShapeKernels.cu.cs")
    {
        string source = RoslynTestUtility.ReadTestCase(relativePath);
        var (_, outputCompilation, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(source, "Cpu");
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
        var loadContext = new AssemblyLoadContext(
            $"SpillOrderMutatingGetter_{testId}_{Guid.NewGuid():N}",
            isCollectible: true);
        return loadContext.LoadFromStream(peStream);
    }

    private static Action<int[], int> CompileAndCreateManagedSeedInvoker(
        string runMethodName,
        string relativePath = "Transpiler/Emission/SpillOrderMutatingGetterShapeKernels.cu.cs",
        string typeName =
            "Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.Emission.SpillOrderMutatingGetterShapeKernels")
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

    private static unsafe Action<int[], int[]> CompileAndCreateManagedRawPointerInvoker(string runMethodName)
    {
        var assembly = CompileManagedShapeAssembly(runMethodName, PointerShapePath);
        var kernelsType = assembly.GetType(PointerShapeTypeName, throwOnError: true);
        Assert.NotNull(kernelsType);
        var runMethod = kernelsType.GetMethod(runMethodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(runMethod);

        return (backing, output) =>
        {
            fixed (int* basePtr = backing)
            {
                object?[] parameters = [Pointer.Box(basePtr, typeof(int*)), output, (uint)ThreadCount];
                runMethod.Invoke(null, parameters);
            }
        };
    }

    private static unsafe Action<int[], int[]> CompileAndCreateManagedPointerInvoker(string runMethodName)
    {
        var assembly = CompileManagedShapeAssembly(runMethodName, PointerShapePath);
        var kernelsType = assembly.GetType(PointerShapeTypeName, throwOnError: true);
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
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_MutatingGetterOnKernelValueParameterAcrossBarrier_PreservesReceiverWriteAfterBarrier()
    {
        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingGetterOnKernelValueParameterAcrossBarrier");
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 10);
        }

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
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_MutatingGetterOnDeviceValueParameterAcrossBarrier_PreservesReceiverWriteAfterBarrier()
    {
        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingGetterOnDeviceValueParameterAcrossBarrier");
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 20);
        }

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
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_MutatingIndexerOnKernelValueParameterAcrossBarrier_PreservesReceiverWriteAfterBarrier()
    {
        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingIndexerOnKernelValueParameterAcrossBarrier");
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 5);
        }

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
    // Shape 4 (#992): Mutating property getter after reading struct field in call arguments
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_MutatingGetterAfterStructIdentifierArgument_SpillsEarlierReadBeforeGetterMutates()
    {
        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingGetterAfterStructIdentifierArgument");
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 10);
        }

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
    // Shape 5 (#992): Mutating indexer getter after reading struct field in call arguments
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_MutatingIndexerAfterStructIdentifierArgument_SpillsEarlierReadBeforeIndexerMutates()
    {
        var invoke = CompileAndCreateManagedSeedInvoker("RunMutatingIndexerAfterStructIdentifierArgument");
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 10);
        }

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
    // #958 (CS0214): External pointer stored in local pointer across barrier on Managed state machine
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_PointerLocalAcrossBarrier_CompilesWithUnsafeContextClassAndMatchesExpected()
    {
        var invoke = CompileAndCreateManagedPointerInvoker("RunPointerLocalAcrossBarrier");
        int[] backing = Enumerable.Range(100, ThreadCount).ToArray();
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(backing, actual);
        }

        int[] expected = Enumerable.Range(100, ThreadCount).Select(v => v + 10).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // #958 (CS0212): Taking address of copied value parameter before barrier on Managed state machine
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_AddressOfValueParameterBeforeBarrier_PinsContextFieldAndMatchesExpected()
    {
        var invoke = CompileAndCreateManagedSeedInvoker(
            "RunAddressOfValueParameterBeforeBarrier",
            PointerShapePath,
            PointerShapeTypeName);
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 40);
        }

        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => 40 + tid + 5 + 3).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // #958 (CS0212): Taking address of hoisted local before barrier on Managed state machine
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_AddressOfHoistedLocalBeforeBarrier_PinsContextFieldAndMatchesExpected()
    {
        var invoke = CompileAndCreateManagedSeedInvoker(
            "RunAddressOfHoistedLocalBeforeBarrier",
            PointerShapePath,
            PointerShapeTypeName);
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 50);
        }

        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => 50 + tid + 7).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // #957: Taking address of value parameter across barrier
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_AddressOfValueParameterAcrossBarrier_PreservesMutation()
    {
        var invoke = CompileAndCreateManagedSeedInvoker(
            "RunAddressOfValueParameterAcrossBarrier",
            PointerShapePath,
            PointerShapeTypeName);
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 100);
        }

        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => 100 + tid + 1).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // #966: Taking address of hoisted local across barrier
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_AddressOfHoistedLocalAcrossBarrier_PreservesMutation()
    {
        var invoke = CompileAndCreateManagedSeedInvoker(
            "RunAddressOfHoistedLocalAcrossBarrier",
            PointerShapePath,
            PointerShapeTypeName);
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 0);
        }

        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => tid * 10 + 1).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // #970: By-value pointer parameter across barrier
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_PointerParameterAcrossBarrier_PreservesValue()
    {
        var invoke = CompileAndCreateManagedRawPointerInvoker("RunPointerParameterAcrossBarrier");
        int[] backing = Enumerable.Range(10, ThreadCount).ToArray();
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(backing, actual);
        }

        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => 10 + tid + tid + 10).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // #1036: Value parameter written through ref local across barrier
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_RefLocalToValueParameterAcrossBarrier_PreservesMutation()
    {
        var invoke = CompileAndCreateManagedSeedInvoker(
            "RunRefLocalToValueParameterAcrossBarrier",
            PointerShapePath,
            PointerShapeTypeName);
        int[] actual = new int[ThreadCount];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            invoke(actual, 200);
        }

        int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => 200 + tid + 1).ToArray();
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // #966: Address of hoisted local across barrier under heavy GC pressure (300 launches)
    // -------------------------------------------------------------------------
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    // Forced blocking compacting collections in a loop stop every other test of the process (about 3 minutes on the
    // Linux CI runner), so it runs with the other Stress tests, not in the normal run
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    public void SpillOrder_AddressOfHoistedLocalAcrossBarrier_StressGcPressure()
    {
        var invoke = CompileAndCreateManagedSeedInvoker(
            "RunAddressOfHoistedLocalAcrossBarrier",
            PointerShapePath,
            PointerShapeTypeName);

        using var scope = GpuExecution.Use(KernelExecution.Managed);
        using var cts = new CancellationTokenSource();
        var gcTask = StartCompactingGcLoop(cts.Token);

        try
        {
            int[] expected = Enumerable.Range(0, ThreadCount).Select(tid => tid * 10 + 1).ToArray();
            int[] actual = new int[ThreadCount];
            for (int i = 0; i < 300; i++)
            {
                Array.Clear(actual, 0, actual.Length);
                invoke(actual, 0);
                Assert.Equal(expected, actual);
            }
        }
        finally
        {
            cts.Cancel();
            gcTask.Wait(TimeSpan.FromSeconds(5));
        }
    }

    private static Task StartCompactingGcLoop(CancellationToken token) =>
        Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
        });
}
