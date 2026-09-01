using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tests.Syntax;

public partial class SYN_28_MethodAttributesTests
:  TranspilerTestBase{
    private const string InlineDevice = "inline __device__";
    private const string ForceinlineDevice = "__forceinline__ __device__";
    private const string NoinlineDevice = "__noinline__ __device__";
    private const string ForceinlineModifier = "__forceinline__";
    private const string NoinlineModifier = "__noinline__";

    [GeneratedRegex($@"{ForceinlineDevice}\s+\w+\s+AggressiveInlinedFunc")]
    private static partial Regex AggressiveInlinedRegex();

    [GeneratedRegex($@"{NoinlineDevice}\s+\w+\s+NoInlinedFunc")]
    private static partial Regex NoInlinedRegex();

    [GeneratedRegex($@"{ForceinlineDevice}\s+\w+\s+MultipleAttributesFunc")]
    private static partial Regex MultipleAttributesRegex();

    [GeneratedRegex($@"{InlineDevice}\s+\w+\s+NoOptimizationFunc")]
    private static partial Regex NoOptimizationRegex();

    [GeneratedRegex($@"{InlineDevice}\s+\w+\s+CustomMethod")]
    private static partial Regex CustomMethodRegex();

    private static (string CudaSource, List<Diagnostic> Diagnostics) TranspileDeviceMethod(string methodName)
    {
        var testCasePath = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Analyzers", "SYN-28_MethodAttributes.cs");
        var compilation = MegaCompilationCache.GetDynamicCompilation(testCasePath);

        var tree = compilation.SyntaxTrees.First(t => t.FilePath == testCasePath);
        var semanticModel = compilation.GetSemanticModel(tree);

        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        var diagnostics = new List<Diagnostic>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice: true);
        return (cudaSource, diagnostics);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_AggressiveInlining_Execution(ITranspilerDialect dialect)
    {
        int[] input = [0, 5, 10, 20];
        int[] result = new int[4];

        SYN_28_MethodAttributesKernelsWrapper.Launch_MethodAttributesKernel(input, result, blockDimX: 4);

        // input 0: (0 + 100) * 2 = 200
        Assert.Equal(200, result[0]);
        // input 5: (5 + 100) * 2 = 210
        Assert.Equal(210, result[1]);
        // input 10: (10 + 100) * 2 = 220
        Assert.Equal(220, result[2]);
        // input 20: (20 + 100) * 2 = 240
        Assert.Equal(240, result[3]);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_MethodAttributes_EmptyArray_Execution(ITranspilerDialect dialect)
    {
        int[] input = [];
        int[] result = [];

        var exception = Record.Exception(() =>
            SYN_28_MethodAttributesKernelsWrapper.Launch_MethodAttributesKernel(input, result, blockDimX: 1));

        Assert.Null(exception);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_MethodAttributes_SingleElement_Execution(ITranspilerDialect dialect)
    {
        int[] input = [42];
        int[] result = new int[1];

        SYN_28_MethodAttributesKernelsWrapper.Launch_MethodAttributesKernel(input, result, blockDimX: 1);

        // (42 + 100) * 2 = 284
        Assert.Equal(284, result[0]);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-28")]
    [InlineData(0, (0 + 100) * 2)]
    [InlineData(1, (1 + 100) * 2)]
    [InlineData(-1, (-1 + 100) * 2)]
    [InlineData(int.MinValue, unchecked((int.MinValue + 100) * 2))]
    [InlineData(int.MaxValue, unchecked((int.MaxValue + 100) * 2))]
    [InlineData(100, (100 + 100) * 2)]
    [InlineData(-100, (-100 + 100) * 2)]
    [InlineData(-500, (-500 + 100) * 2)]
    [InlineData(0x55555555, unchecked((0x55555555 + 100) * 2))]
    [InlineData(unchecked((int)0xAAAAAAAA), unchecked(((int)0xAAAAAAAA + 100) * 2))]
    [InlineData(1000000, (1000000 + 100) * 2)]
    [InlineData(-1000000, (-1000000 + 100) * 2)]
    public void Test_SYN28_AggressiveInlining_NumericBoundaries_Execution(int inputVal, int expectedVal)
    {
        int[] input = [inputVal];
        int[] result = new int[1];

        SYN_28_MethodAttributesKernelsWrapper.Launch_MethodAttributesKernel(input, result, blockDimX: 1);

        Assert.Equal(expectedVal, result[0]);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [SkippableTheory]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_AggressiveInlining_TranspilesWithForceinlineDevice(ITranspilerDialect dialect)
    {
        Skip.IfNot(dialect.IsCuda, "Syntax Test: Method attributes compile to __device__ inline which is CUDA specific.");
        var (cudaSource, diagnostics) = TranspileDeviceMethod("AggressiveInlinedFunc");

        Assert.Empty(diagnostics);
        Assert.Matches(AggressiveInlinedRegex(), cudaSource);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [SkippableTheory]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_NoInlining_TranspilesWithNoinlineDevice(ITranspilerDialect dialect)
    {
        Skip.IfNot(dialect.IsCuda, "Syntax Test: Method attributes compile to __device__ inline which is CUDA specific.");
        var (cudaSource, diagnostics) = TranspileDeviceMethod("NoInlinedFunc");

        Assert.Empty(diagnostics);
        Assert.Matches(NoInlinedRegex(), cudaSource);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [SkippableTheory]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_MultipleAttributes_EmitsForceinlineOnceWithoutDuplicates(ITranspilerDialect dialect)
    {
        Skip.IfNot(dialect.IsCuda, "Syntax Test: Method attributes compile to __device__ inline which is CUDA specific.");
        var (cudaSource, diagnostics) = TranspileDeviceMethod("MultipleAttributesFunc");

        Assert.Empty(diagnostics);
        Assert.Matches(MultipleAttributesRegex(), cudaSource);

        var matches = Regex.Matches(cudaSource, ForceinlineModifier);
        Assert.Single(matches);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [SkippableTheory]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_UnsupportedOption_NoOptimization_DefaultsToInlineDeviceWithoutError(ITranspilerDialect dialect)
    {
        Skip.IfNot(dialect.IsCuda, "Syntax Test: Method attributes compile to __device__ inline which is CUDA specific.");
        var (cudaSource, diagnostics) = TranspileDeviceMethod("NoOptimizationFunc");

        Assert.Empty(diagnostics);
        Assert.Matches(NoOptimizationRegex(), cudaSource);
        Assert.DoesNotMatch(ForceinlineModifier, cudaSource);
        Assert.DoesNotMatch(NoinlineModifier, cudaSource);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [SkippableTheory]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_UserDefinedMethodImplAttribute_DoesNotCollideWithBclMethodImpl(ITranspilerDialect dialect)
    {
        Skip.IfNot(dialect.IsCuda, "Syntax Test: Method attributes compile to __device__ inline which is CUDA specific.");
        var (cudaSource, diagnostics) = TranspileDeviceMethod("CustomMethod");

        Assert.Empty(diagnostics);
        Assert.Matches(CustomMethodRegex(), cudaSource);
        Assert.DoesNotMatch(ForceinlineModifier, cudaSource);
        Assert.DoesNotMatch(NoinlineModifier, cudaSource);
    }
}
