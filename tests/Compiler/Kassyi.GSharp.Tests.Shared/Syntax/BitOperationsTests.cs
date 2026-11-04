using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class BitOperationsTests : TranspilerTestBase
{
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
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

        BitOperationsKernels.LaunchRotate32Kernel(vals, shifts, rotLeft, rotRight, blockDimX: 1);

        Assert.Equal(BitOperations.RotateLeft(val, shift), rotLeft[0]);
        Assert.Equal(BitOperations.RotateRight(val, shift), rotRight[0]);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
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

        BitOperationsKernels.LaunchRotate64Kernel(vals, shifts, rotLeft, rotRight, blockDimX: 1);

        Assert.Equal(BitOperations.RotateLeft(val, shift), rotLeft[0]);
        Assert.Equal(BitOperations.RotateRight(val, shift), rotRight[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_BCL02_BitOperations_Rotate32_TranspilesToPtxFunnelShiftIntrinsics()
    {
        // 1. Verify wrapper source uses G# rotation prelude intrinsics
        string source = BitOperationsKernels.Rotate32Kernel_Source;
        Assert.Contains("__gsharp_rotl32", source);
        Assert.Contains("__gsharp_rotr32", source);

        // 2. Verify MethodTranspiler generates __gsharp_rotl32 and __gsharp_rotr32 from AST
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod(typeof(BitOperationsKernels), "Rotate32Kernel");

        Assert.Empty(diagnostics);
        Assert.Contains("__gsharp_rotl32", cudaSource);
        Assert.Contains("__gsharp_rotr32", cudaSource);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BclIntrinsic_Interlocked_ReorderedNamedArguments_Int()
    {
        int[] targets = [10, 20, 30, 40];
        int[] results = new int[4];

        BitOperationsKernels.LaunchInterlockedReorderedNamedArgsKernel(targets, results, blockDimX: 1);

        Assert.Equal([99, 77, 35, 42], targets);
        Assert.Equal([10, 20, 35, 40], results);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BclIntrinsic_Interlocked_ReorderedNamedArguments_Long()
    {
        long[] targets = [100L, 200L, 300L];
        long[] results = new long[3];

        BitOperationsKernels.LaunchInterlockedLongReorderedNamedArgsKernel(targets, results, blockDimX: 1);

        Assert.Equal([999L, 250L, 420L], targets);
        Assert.Equal([100L, 250L, 300L], results);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BclIntrinsic_BitOperations_Rotation_ReorderedNamedArguments_UInt()
    {
        uint[] values = [0x12345678u];
        uint[] results = new uint[2];

        BitOperationsKernels.LaunchRotationReorderedNamedArgsKernel(values, results, blockDimX: 1);

        Assert.Equal(
            [
                BitOperations.RotateLeft(0x12345678u, 4),
                BitOperations.RotateRight(0x12345678u, 4)
            ],
            results);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BclIntrinsic_BitOperations_Rotation_ReorderedNamedArguments_ULong()
    {
        ulong[] values = [0x123456789ABCDEF0ul];
        ulong[] results = new ulong[2];

        BitOperationsKernels.LaunchRotationLongReorderedNamedArgsKernel(values, results, blockDimX: 1);

        Assert.Equal(
            [
                BitOperations.RotateLeft(0x123456789ABCDEF0ul, 8),
                BitOperations.RotateRight(0x123456789ABCDEF0ul, 8)
            ],
            results);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BclIntrinsic_ReorderedNamedArguments_PreservesLeftToRightEvaluationOrder()
    {
        int[] targets = [10];
        int[] intResults = new int[1];
        uint[] u32Results = new uint[1];

        BitOperationsKernels.LaunchReorderedNamedArgsEvaluationOrderKernel(targets, intResults, u32Results, blockDimX: 1);

        Assert.Equal([110], targets);
        Assert.Equal([10], intResults);
        Assert.Equal([BitOperations.RotateLeft(5u, 4)], u32Results);
    }
}
