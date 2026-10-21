
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigIntBitwiseTests
{
    public static IEnumerable<object[]> GetBitwiseTestData()
    {
        // Zero boundaries
        yield return new object[] { new uint[] { 0, 0, 0, 0 }, new uint[] { 0, 0, 0, 0 } };
        // Max value boundaries
        yield return new object[] { new uint[] { 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF }, new uint[] { 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF } };
        // Alternating bit pattern boundaries
        yield return new object[] { new uint[] { 0x55555555, 0x55555555, 0x55555555, 0x55555555 }, new uint[] { 0xAAAAAAAA, 0xAAAAAAAA, 0xAAAAAAAA, 0xAAAAAAAA } };
        // General values
        yield return new object[] { new uint[] { 1, 2, 3, 4 }, new uint[] { 4, 3, 2, 1 } };
        // Random bit patterns
        yield return new object[] { new uint[] { 0xFF00FF00, 0x00FF00FF, 0x12345678, 0x87654321 }, new uint[] { 0x00FF00FF, 0xFF00FF00, 0x87654321, 0x12345678 } };
    }

    [SkippableTheory]
    [MemberData(nameof(GetBitwiseTestData))]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BitwiseAnd(uint[] aWords, uint[] bWords)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(ctx, aWords);
        using var b = new GpuBigUInt(ctx, bWords);
        using var r = new GpuBigUInt(ctx, aWords.Length);

        GpuBigIntMath.BitwiseAnd(a, b, r);
        ctx.Provider.SynchronizeStream(ctx.Stream);

        var rWords = r.ToHostArray();
        for (int i = 0; i < aWords.Length; i++)
        {
            Assert.Equal(aWords[i] & bWords[i], rWords[i]);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(GetBitwiseTestData))]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BitwiseOr(uint[] aWords, uint[] bWords)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(ctx, aWords);
        using var b = new GpuBigUInt(ctx, bWords);
        using var r = new GpuBigUInt(ctx, aWords.Length);

        GpuBigIntMath.BitwiseOr(a, b, r);
        ctx.Provider.SynchronizeStream(ctx.Stream);

        var rWords = r.ToHostArray();
        for (int i = 0; i < aWords.Length; i++)
        {
            Assert.Equal(aWords[i] | bWords[i], rWords[i]);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(GetBitwiseTestData))]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BitwiseXor(uint[] aWords, uint[] bWords)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(ctx, aWords);
        using var b = new GpuBigUInt(ctx, bWords);
        using var r = new GpuBigUInt(ctx, aWords.Length);

        GpuBigIntMath.BitwiseXor(a, b, r);
        ctx.Provider.SynchronizeStream(ctx.Stream);

        var rWords = r.ToHostArray();
        for (int i = 0; i < aWords.Length; i++)
        {
            Assert.Equal(aWords[i] ^ bWords[i], rWords[i]);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(GetBitwiseTestData))]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BitwiseNot(uint[] aWords, uint[] _)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(ctx, aWords);
        using var r = new GpuBigUInt(ctx, aWords.Length);

        GpuBigIntMath.BitwiseNot(a, r);
        ctx.Provider.SynchronizeStream(ctx.Stream);

        var rWords = r.ToHostArray();
        for (int i = 0; i < aWords.Length; i++)
        {
            Assert.Equal(~aWords[i], rWords[i]);
        }
    }
}
