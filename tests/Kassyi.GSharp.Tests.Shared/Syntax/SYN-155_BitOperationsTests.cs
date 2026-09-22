using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_155_BitOperationsTests : TranspilerTestBase
{
    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "155")]
    [InlineData(0x12345678u, 0)]
    [InlineData(0x12345678u, 31)]
    [InlineData(0x12345678u, 32)]
    [InlineData(0x12345678u, 35)]
    [InlineData(0u, 5)]
    [InlineData(uint.MaxValue, 16)]
    [InlineData(0x80000000u, 1)]
    [InlineData(0x55555555u, 31)]
    public void Test_BCL02_Rotate32_Execution(uint val, int shift)
    {

        uint[] vals = [val];
        int[] shifts = [shift];
        uint[] rotLeft = new uint[1];
        uint[] rotRight = new uint[1];

        SYN_155_BitOperationsKernelsWrapper.LaunchRotate32Kernel(vals, shifts, rotLeft, rotRight, blockDimX: 1);

        Assert.Equal(BitOperations.RotateLeft(val, shift), rotLeft[0]);
        Assert.Equal(BitOperations.RotateRight(val, shift), rotRight[0]);
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "155")]
    [InlineData(0x123456789ABCDEF0ul, 0)]
    [InlineData(0x123456789ABCDEF0ul, 63)]
    [InlineData(0x123456789ABCDEF0ul, 64)]
    [InlineData(0x123456789ABCDEF0ul, 70)]
    [InlineData(0ul, 10)]
    [InlineData(ulong.MaxValue, 33)]
    [InlineData(0x8000000000000000ul, 1)]
    [InlineData(0x5555555555555555ul, 63)]
    public void Test_BCL02_Rotate64_Execution(ulong val, int shift)
    {

        ulong[] vals = [val];
        int[] shifts = [shift];
        ulong[] rotLeft = new ulong[1];
        ulong[] rotRight = new ulong[1];

        SYN_155_BitOperationsKernelsWrapper.LaunchRotate64Kernel(vals, shifts, rotLeft, rotRight, blockDimX: 1);

        Assert.Equal(BitOperations.RotateLeft(val, shift), rotLeft[0]);
        Assert.Equal(BitOperations.RotateRight(val, shift), rotRight[0]);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "155")]
    public void Test_BCL02_BitOperations_Rotate32_TranspilesToPtxFunnelShiftIntrinsics()
    {
        // 1. Verify wrapper source uses G# rotation prelude intrinsics
        string source = SYN_155_BitOperationsKernelsWrapper.Rotate32Kernel_Source;
        Assert.Contains("__gsharp_rotl32", source);
        Assert.Contains("__gsharp_rotr32", source);

        // 2. Verify MethodTranspiler generates __gsharp_rotl32 and __gsharp_rotr32 from AST
        var kernelPath = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "..", "Syntax", "SYN-155_BitOperationsKernels.cu.cs");
        var kernelCode = File.ReadAllText(kernelPath);
        var compilation = RoslynTestUtility.CreateCompilation(kernelCode);
        var tree = compilation.SyntaxTrees.First();
        var semanticModel = compilation.GetSemanticModel(tree);

        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "Rotate32Kernel");

        var diagnostics = new List<Diagnostic>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice: false);

        Assert.Empty(diagnostics);
        Assert.Contains("__gsharp_rotl32", cudaSource);
        Assert.Contains("__gsharp_rotr32", cudaSource);
    }
}
