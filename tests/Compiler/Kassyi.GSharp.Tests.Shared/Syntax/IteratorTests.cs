using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
#endif

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A <c>[GpuDeviceFunction]</c> returning <see cref="System.Collections.Generic.IEnumerable{T}"/> with <c>yield</c>
/// compiles to a value-type enumerator and produces the same sequence on CUDA, CPU AOT, and managed execution.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
#if GSHARP_CPU
[Collection(TestCollections.GlobalStateSequential)]
#endif
public class IteratorTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Range_SumsArithmeticProgressionPerThread()
    {
        const int threads = 8;
        const int count = 4;
        var output = new int[threads];

        IteratorKernels.LaunchRangeKernel(output, count, blockDimX: threads);

        for (int tid = 0; tid < threads; tid++)
        {
            Assert.Equal(4 * tid + 6, output[tid]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Evens_FiltersArrayWithForEachAndYield()
    {
        int[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var output = new int[1];

        IteratorKernels.LaunchEvensKernel(data, data.Length, output, blockDimX: 1);

        Assert.Equal(20, output[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Fibonacci_PreservesLocalsAcrossYields()
    {
        var lastOutput = new int[1];
        var sumOutput = new int[1];

        IteratorKernels.LaunchFibonacciKernel(10, lastOutput, sumOutput, blockDimX: 1);

        Assert.Equal(34, lastOutput[0]);
        Assert.Equal(88, sumOutput[0]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(5, 10)]
    public void Test_Countdown_EnumeratingSameLocalTwiceRestoresInitialParameter(int n, int expectedSum)
    {
        var firstSum = new int[1];
        var secondSum = new int[1];

        IteratorKernels.LaunchCountdownTwiceKernel(n, firstSum, secondSum, blockDimX: 1);

        Assert.Equal(expectedSum, firstSum[0]);
        Assert.Equal(expectedSum, secondSum[0]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0)]
    [InlineData(5, 6)]
    [InlineData(100, 21)]
    public void Test_TakeUntil_YieldBreakTerminatesEnumerationEarly(int limit, int expectedSum)
    {
        int[] data = [1, 2, 3, 6, 9];
        var output = new int[1];

        IteratorKernels.LaunchTakeUntilKernel(data, limit, output, blockDimX: 1);

        Assert.Equal(expectedSum, output[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Doubled_NestedIteratorForEachDoublesRangeSum()
    {
        const int threads = 8;
        const int count = 4;
        var output = new int[threads];

        IteratorKernels.LaunchDoubledKernel(output, count, blockDimX: threads);

        for (int tid = 0; tid < threads; tid++)
        {
            Assert.Equal(2 * (4 * tid + 6), output[tid]);
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0, 0)]
    [InlineData(3, 63)]
    [InlineData(4, 106)]
    public void Test_StructItems_YieldsNestedUserDefinedStructs(int count, int expectedBaseSum)
    {
        const int threads = 4;
        var output = new int[threads];

        IteratorKernels.LaunchStructIteratorKernel(output, count, blockDimX: threads);

        for (int tid = 0; tid < threads; tid++)
        {
            Assert.Equal(expectedBaseSum + count * tid, output[tid]);
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(5, 3, -1, 39)]
    [InlineData(10, 0, 3, 1032)]
    public void Test_ConsumerControlFlow_BreakContinueReturnAndNestedLoops(
        int stopAt,
        int skipVal,
        int earlyReturnAt,
        int expected)
    {
        var output = new int[1];

        IteratorKernels.LaunchConsumerControlFlowKernel(stopAt, skipVal, earlyReturnAt, output, blockDimX: 1);

        Assert.Equal(expected, output[0]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0, 2, 1112)]
    [InlineData(1, 2, 1125)]
    [InlineData(0, 99, 1552)]
    [InlineData(9, 2, 0)]
    public void Test_MultiYieldLabelAndShadow_DuplicatesBodyWithDistinctLabelsAndHygienicNames(
        int mode,
        int baseVal,
        int expected)
    {
        var output = new int[1];

        IteratorKernels.LaunchMultiYieldLabelAndShadowKernel(mode, baseVal, output, blockDimX: 1);

        Assert.Equal(expected, output[0]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1, 30, 100)]
    [InlineData(2, 100, 100)]
    public void Test_UsingAndSpan_DisposesOnEarlyBreakAndNormalCompletion(
        int maxItems,
        int expectedSum,
        int expectedDisposeMarker)
    {
        int[] data = [10, 20, 30, 40];
        var output = new int[1];
        var disposeLog = new int[1];

        IteratorKernels.LaunchUsingAndSpanBreakKernel(data, maxItems, output, disposeLog, blockDimX: 1);

        Assert.Equal(expectedSum, output[0]);
        Assert.Equal(expectedDisposeMarker, disposeLog[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_NamedAndDefaultArgs_EvaluatesLexicallyAndAppliesDefaults()
    {
        var output = new int[3];
        var evalLog = new int[4];

        IteratorKernels.LaunchNamedAndDefaultArgsKernel(output, evalLog, blockDimX: 1);

        Assert.Equal([39, 42, 21], output);
        Assert.Equal([1, 2, 3, 4], evalLog);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_WideningElementType_VarUsesDeclaredIteratorElementType()
    {
        var output = new long[1];

        IteratorKernels.LaunchWideningElementTypeKernel(output, blockDimX: 1);

        Assert.Equal(int.MaxValue + 1L, output[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InlineArrayForEach_EnumeratesBufferElementsInsideIterator()
    {
        const int threads = 4;
        const int scale = 3;
        var output = new int[threads];

        IteratorKernels.LaunchInlineArrayForEachKernel(scale, output, blockDimX: threads);

        for (int tid = 0; tid < threads; tid++)
        {
            Assert.Equal((4 * tid + 10) * scale, output[tid]);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ExternalAssemblyIterator_ReportsUnsupportedSyntax()
    {
        var externalLibrarySource = RoslynTestUtility.ReadTestCase("Analyzers/IteratorExternalAssembly_Lib.cs");
        var consumerSource = RoslynTestUtility.ReadTestCase("Analyzers/IteratorExternalAssembly_Consumer.cu.cs");

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var externalComp = CSharpCompilation.Create(
            "ExternalIteratorLib",
            [CSharpSyntaxTree.ParseText(externalLibrarySource, parseOptions)],
            RoslynTestUtility.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var emitResult = externalComp.Emit(peStream);
        Assert.True(emitResult.Success);
        peStream.Position = 0;
        var externalRef = MetadataReference.CreateFromStream(peStream);

        var consumerComp = CSharpCompilation.Create(
            "ConsumerApp",
            [CSharpSyntaxTree.ParseText(consumerSource, parseOptions, path: "IteratorExternalAssembly_Consumer.cu.cs")],
            RoslynTestUtility.BaseReferences.Append(externalRef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var run = await KernelDiagnostics.RunCompilationAsync(consumerComp, DialectName);

        var actual = run.Analyzer
            .Select(static d =>
            {
                var span = d.Location.GetLineSpan().Span;
                return (
                    d.Id,
                    span.Start.Line + 1,
                    span.Start.Character + 1,
                    span.End.Line + 1,
                    span.End.Character + 1);
            })
            .ToList();

        Assert.Equal(
            [
                (GSharpDiagnostics.UnsupportedSyntax.Id, 13, 9, 16, 10),
                (GSharpDiagnostics.UnsupportedSyntax.Id, 19, 9, 22, 10)
            ],
            actual);
        Assert.True(run.Analyzer.All(static d => d.GetMessage().Contains("foreach of an iterator that has no body in this compilation")));
        var stated = KernelDiagnostics.FindStatedFailures(run);
        Assert.Equal(2, stated.Count);
        Assert.All(stated, static f => Assert.EndsWith($": {GSharpDiagnostics.UnsupportedSyntax.Id}", f.Failure));
        Assert.Empty(stated.Where(static f => !f.IsReported));
    }

#if GSHARP_CPU
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_AllIteratorKernels_MatchNativeSemantics()
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            var rangeOut = new int[4];
            IteratorKernels.LaunchRangeKernel(rangeOut, 4, blockDimX: 4);
            Assert.Equal([6, 10, 14, 18], rangeOut);

            int[] data = [1, 2, 3, 4, 5, 6, 7, 8];
            var evensOut = new int[1];
            IteratorKernels.LaunchEvensKernel(data, data.Length, evensOut, blockDimX: 1);
            Assert.Equal(20, evensOut[0]);

            var fibLast = new int[1];
            var fibSum = new int[1];
            IteratorKernels.LaunchFibonacciKernel(10, fibLast, fibSum, blockDimX: 1);
            Assert.Equal(34, fibLast[0]);
            Assert.Equal(88, fibSum[0]);

            var cFirst = new int[1];
            var cSecond = new int[1];
            IteratorKernels.LaunchCountdownTwiceKernel(5, cFirst, cSecond, blockDimX: 1);
            Assert.Equal(10, cFirst[0]);
            Assert.Equal(10, cSecond[0]);

            int[] takeData = [1, 2, 3, 6, 9];
            var takeOut = new int[1];
            IteratorKernels.LaunchTakeUntilKernel(takeData, 5, takeOut, blockDimX: 1);
            Assert.Equal(6, takeOut[0]);

            var doubledOut = new int[4];
            IteratorKernels.LaunchDoubledKernel(doubledOut, 4, blockDimX: 4);
            Assert.Equal([12, 20, 28, 36], doubledOut);

            var structOut = new int[4];
            IteratorKernels.LaunchStructIteratorKernel(structOut, 3, blockDimX: 4);
            Assert.Equal([63, 66, 69, 72], structOut);

            var cfOut = new int[1];
            IteratorKernels.LaunchConsumerControlFlowKernel(5, 3, -1, cfOut, blockDimX: 1);
            Assert.Equal(39, cfOut[0]);

            var multiOut = new int[1];
            IteratorKernels.LaunchMultiYieldLabelAndShadowKernel(1, 2, multiOut, blockDimX: 1);
            Assert.Equal(1125, multiOut[0]);

            int[] spanData = [10, 20, 30, 40];
            var spanOut = new int[1];
            var dispLog = new int[1];
            IteratorKernels.LaunchUsingAndSpanBreakKernel(spanData, 1, spanOut, dispLog, blockDimX: 1);
            Assert.Equal(30, spanOut[0]);
            Assert.Equal(100, dispLog[0]);

            var namedOut = new int[3];
            var evalLog = new int[4];
            IteratorKernels.LaunchNamedAndDefaultArgsKernel(namedOut, evalLog, blockDimX: 1);
            Assert.Equal([39, 42, 21], namedOut);
            Assert.Equal([1, 2, 3, 4], evalLog);

            var wideningOut = new long[1];
            IteratorKernels.LaunchWideningElementTypeKernel(wideningOut, blockDimX: 1);
            Assert.Equal(int.MaxValue + 1L, wideningOut[0]);

            var inlineArrOut = new int[4];
            IteratorKernels.LaunchInlineArrayForEachKernel(3, inlineArrOut, blockDimX: 4);
            Assert.Equal([30, 42, 54, 66], inlineArrOut);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
#endif
}
