
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_46_50_ConcurrencyAndWarpTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-47")]
    public void TestSharedMemoryKernel()
    {
        int[] dataArray = [1, 2, 3, 4];

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_SharedMemoryKernel(dataArray, 4, blockDimX: 4, sharedMemBytes: 32);

        Assert.Equal(3, dataArray[0]);
        Assert.Equal(6, dataArray[1]);
        Assert.Equal(9, dataArray[2]);
        Assert.Equal(12, dataArray[3]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-47")]
    public void TestSharedMemory_WarpSyncAcross256Threads()
    {
        const int size = 256;
        int[] dataArray = new int[size];
        for (int i = 0; i < size; i++)
        {
            dataArray[i] = i * 3 + 1;
        }

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_SharedMemoryReverseKernel(
            dataArray,
            size,
            blockDimX: size
        );

        for (int i = 0; i < size; i++)
        {
            int expected = (size - 1 - i) * 3 + 1;
            Assert.Equal(expected, dataArray[i]);
        }
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-49")]
    [Trait("FeatureId", "SYN-68")]
    public void TestIntrinsicsKernel()
    {
        int[] dataArray = new int[2];
        dataArray[0] = 0; // atomic sum
        dataArray[1] = 0; // max value

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_IntrinsicsKernel(dataArray, blockDimX: 10);

        Assert.Equal(45, dataArray[0]); // Sum of 0..9 is 45
        Assert.Equal(100, dataArray[1]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("Category", "Stress")]
    [Trait("FeatureId", "SYN-49")]
    public void TestAtomicAddStress_HighContention()
    {
        int[] globalSum = [0];
        uint threadsPerBlock = 256;
        uint blocksPerGrid = 16;
        int expectedTotalThreads = (int)(threadsPerBlock * blocksPerGrid); // 4096 threads

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_AtomicAddStressKernel(
            globalSum,
            gridDimX: blocksPerGrid,
            blockDimX: threadsPerBlock
        );

        Assert.Equal(expectedTotalThreads, globalSum[0]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("Category", "Stress")]
    [Trait("FeatureId", "SYN-49")]
    public void TestAtomicOrMaxStress_HighContention()
    {
        uint[] globalState = [0, 0];
        uint threadsPerBlock = 256;
        uint blocksPerGrid = 16;
        uint expectedTotalThreads = threadsPerBlock * blocksPerGrid; // 4096 threads

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_AtomicOrMaxStressKernel(
            globalState,
            gridDimX: blocksPerGrid,
            blockDimX: threadsPerBlock
        );

        // First 31 threads will OR their bits: 1 << 0 to 1 << 30
        uint expectedOr = int.MaxValue; // 0x7FFFFFFF
        Assert.Equal(expectedOr, globalState[0]);

        // Max thread ID will be 4095
        Assert.Equal(expectedTotalThreads - 1, globalState[1]);
    }

    [Fact]
    [Trait("FeatureId", "SYN-46")]
    [Trait("Category", "Syntax")]
    public void Test_SYN46_SyncThreads_Transpilation()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public static class TestWarpKernel
        {
            [GpuKernel]
            public static void Kernel(int[] data)
            {
                data[0] = 1;
                Gpu.SyncThreads();
                data[0] = 2;
            }
        }
        """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var syntaxTree = compilation.SyntaxTrees.Last();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodDecl = syntaxTree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First();

        var cudaSource = Kassyi.GSharp.Compiler.Transpiler.MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel);

        Assert.Contains("__syncthreads()", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "SYN-46")]
    [Trait("Category", "Syntax")]
    public void Test_SYN46_WarpShuffle_Transpilation()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public static class TestWarpKernel
        {
            [GpuKernel]
            public static void Kernel(int[] data)
            {
                int val = data[ThreadIndex.X];
                int shuffled = Gpu.WarpShuffle(val, 0);
                data[ThreadIndex.X] = shuffled;
            }
        }
        """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var syntaxTree = compilation.SyntaxTrees.Last();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodDecl = syntaxTree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First();

        var cudaSource = Kassyi.GSharp.Compiler.Transpiler.MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel);

        Assert.Contains("__shfl_sync(", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "SYN-46")]
    [Trait("Category", "SadPath")]
    public void Test_SYN46_LockStatement_ReportsDiagnostic()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public static class TestLockKernel
        {
            [GpuKernel]
            public static void Kernel(int[] data)
            {
                object obj = null;
                lock(obj)
                {
                    data[0] = 1;
                }
            }
        }
        """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var syntaxTree = compilation.SyntaxTrees.Last();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodDecl = syntaxTree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        Kassyi.GSharp.Compiler.Transpiler.MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d));

        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "SYN-46")]
    [Trait("Category", "Syntax")]
    public void Test_SYN46_WarpShuffle_DefaultMaskAndPathConsistency_Transpilation()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public static class TestWarpKernel
        {
            [GpuKernel]
            public static void Kernel(int[] data)
            {
                int val = data[ThreadIndex.X];
                // Default mask argument (0xffffffff) omitted in C# call
                int s1 = Gpu.WarpShuffle(val, 0);
                // Explicit mask argument (0xffffffff) in C# call
                int s2 = Gpu.WarpShuffle(val, 1, 0xffffffffu);
                data[ThreadIndex.X] = s1 + s2;
            }
        }
        """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var syntaxTree = compilation.SyntaxTrees.Last();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodDecl = syntaxTree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First();

        var cudaSource = Kassyi.GSharp.Compiler.Transpiler.MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel);

        Assert.Contains("__shfl_sync(", cudaSource);
        Assert.Contains("0xffffffff", cudaSource.ToLowerInvariant());
    }

    [Fact]
    [Trait("FeatureId", "SYN-49")]
    [Trait("Category", "Syntax")]
    public void Test_SYN49_AtomicAddAndInterlocked_Transpilation()
    {
        var source = """
        using Kassyi.GSharp.Core;
        using System.Threading;

        public static class TestAtomicKernel
        {
            [GpuKernel]
            public static void Kernel(int[] data)
            {
                Gpu.AtomicAdd(ref data[0], 5);
                Interlocked.Add(ref data[1], 10);
            }
        }
        """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var syntaxTree = compilation.SyntaxTrees.Last();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodDecl = syntaxTree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First();

        var cudaSource = Kassyi.GSharp.Compiler.Transpiler.MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel);

        Assert.Contains("atomicAdd", cudaSource);
        Assert.Contains("data[0]", cudaSource);
        Assert.Contains("data[1]", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "SYN-49")]
    [Trait("Category", "SadPath")]
    public void Test_SYN49_AtomicOnUnsupportedType_ReportsDiagnosticOrCompilerError()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public struct CustomUnsupportedStruct
        {
            public int Field1;
        }

        public static class TestAtomicKernel
        {
            [GpuKernel]
            public static void Kernel(CustomUnsupportedStruct[] data)
            {
                Gpu.AtomicAdd(ref data[0], new CustomUnsupportedStruct());
            }
        }
        """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var diagnostics = compilation.GetDiagnostics();

        // Gpu.AtomicAdd is only overloaded for int, uint, ulong, float.
        // Calling it with CustomUnsupportedStruct generates a C# compiler error (CS1503 / CS0121 etc).
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact(Skip = "未実装: SYN-48 (グリッド協調グループ同期 CooperativeGroups)")]
    [Trait("FeatureId", "SYN-48")]
    [Trait("Category", "Syntax")]
    public void Test_SYN48_CooperativeGroups_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-50 (非同期コピー / TMA Tensor Memory Accelerator)")]
    [Trait("FeatureId", "SYN-50")]
    [Trait("Category", "Syntax")]
    public void Test_SYN50_AsyncCopyTma_Transpilation()
    {
    }
}
