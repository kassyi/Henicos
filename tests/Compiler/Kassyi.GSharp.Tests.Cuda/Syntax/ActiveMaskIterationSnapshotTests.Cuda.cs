#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Cuda.Syntax;

[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ActiveMaskIterationSnapshotTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task GenerateCudaSource_GotoLoopThroughCountedForLoop_EmitsNestedMatchAnyKeys()
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static class SnapshotKernels
            {
                [GpuKernel]
                public static void GotoThroughForKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    int p = -1;
                L1:
                    p++;
                    result[t + 32 * p] = Warp.ActiveMask();
                    for (int i = 0; i < 2; i++)
                    {
                        if (p < 1 && (t & 3) == i + 1)
                        {
                            goto L1;
                        }

                        result[t + 32 * (2 + 2 * p + i)] = Warp.ActiveMask();
                    }

                    result[t + 192] = Warp.ActiveMask();
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var failures = new List<TranspileFailure>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(
            methodDecl,
            model,
            failures.Add,
            false,
            new GSharpCudaDialect(),
            out _);

        Assert.Empty(failures);
        return Verify(cudaSource);
    }

    [Theory]
    [InlineData("ConditionalAccessInUniformLoopKernel", "__match_any_sync(__activemask(),(unsigned int)")]
    [InlineData("BreakBlockConditionalAccessKernel", "__match_any_sync(__activemask(),(unsigned int)")]
    [InlineData("ChainedConditionalAccessInLoopKernel", "__match_any_sync(__activemask(),(unsigned int)")]
    [InlineData("ConditionalAccessArgumentInLoopKernel", "__match_any_sync(__activemask(),(unsigned int)")]
    [InlineData("DivergentForConditionalAccessKernel", "__match_any_sync(__activemask(),__gsharp_iteration_0)")]
    [Scenario(TestScenario.Boundary)]
    public void GenerateCudaSource_ConditionalAccessInLoop_EmitsMatchAnyKeys(string kernelName, string expectedSnippet)
    {
        const string source = """
            #nullable enable
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public struct MaskBox
            {
                public uint[]? Row;
                public uint Last;

                [GpuDeviceFunction]
                public readonly uint Combine(uint mask) => Last | mask;
            }

            public static class ConditionalAccessKernels
            {
                [GpuKernel]
                public static void ConditionalAccessInUniformLoopKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    uint[]? row = (t & 1) == 0 ? result : null;
                    for (int i = 0; i < 2; i++)
                    {
                        row?[t + 32 * i] = Warp.ActiveMask();
                    }
                }

                [GpuKernel]
                public static void BreakBlockConditionalAccessKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    uint[]? row = result;
                    for (int i = 0; i < 4; i++)
                    {
                        row?[t + 32 * i] = Warp.ActiveMask();
                        if (i == (t & 3))
                        {
                            row?[t + 128] = Warp.ActiveMask();
                            break;
                        }
                    }
                }

                [GpuKernel]
                public static void ChainedConditionalAccessInLoopKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    MaskBox? box = (t & 1) == 0 ? new MaskBox { Row = result } : null;
                    for (int i = 0; i < 2; i++)
                    {
                        box?.Row?[t + 32 * i] = Warp.ActiveMask();
                    }
                }

                [GpuKernel]
                public static void ConditionalAccessArgumentInLoopKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    MaskBox? box = (t & 1) == 0 ? new MaskBox { Row = result } : null;
                    for (int i = 0; i < 2; i++)
                    {
                        result[t + 32 * i] = box?.Combine(Warp.ActiveMask()) ?? 0u;
                    }
                }

                [GpuKernel]
                public static void DivergentForConditionalAccessKernel(uint[] result)
                {
                    int t = ThreadIndex.X;
                    uint[]? row = result;
                    for (int i = t & 1; i < 2; i++)
                    {
                        row?[t + 32 * i] = Warp.ActiveMask();
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == kernelName);
        var failures = new List<TranspileFailure>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(
            methodDecl,
            model,
            failures.Add,
            false,
            new GSharpCudaDialect(),
            out _);

        Assert.Empty(failures);
        Assert.Contains(expectedSnippet, cudaSource);
        Assert.DoesNotContain("= __activemask()", cudaSource);
        Assert.DoesNotContain("(__activemask())", cudaSource);
    }
}
