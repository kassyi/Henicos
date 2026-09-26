using System;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Providers;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;
using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Xunit;
using VerifyXunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests;

public class GBF01_GpuBigFloatArithmeticTests
{
    private BigIntExecutionContext CreateContext(GpuContext ctx)
    {
        return new BigIntExecutionContext(ctx, ctx.MemoryManager, default!, ctx.Stream);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    public Task Test_GBF01_CpuReference_Divide()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuContext(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 100 * 2^10 / 3 * 2^5
        using var leftSig = new GpuUIntBigInteger(ctx, new uint[] { 100, 0, 0, 0 });
        int leftExp = 10;
        bool leftSign = false;

        using var rightSig = new GpuUIntBigInteger(ctx, new uint[] { 3, 0, 0, 0 });
        int rightExp = 5;
        bool rightSign = false;

        using var resultSig = new GpuUIntBigInteger(ctx, 4);

        CSharpBigFloatArithmetic.Instance.Divide(ref execCtx, 
            leftSig.Handle, leftExp, leftSign,
            rightSig.Handle, rightExp, rightSign,
            resultSig.Handle, out int resultExp, out bool resultSign);

        var result = new 
        { 
            ResultWords = resultSig.ToHostArray(),
            ResultExp = resultExp,
            ResultSign = resultSign 
        };
        return Verify(result);
    }
    
    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    public Task Test_GBF01_CpuReference_Reciprocal()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuContext(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 1 / (3 * 2^0)
        using var valueSig = new GpuUIntBigInteger(ctx, new uint[] { 3, 0, 0, 0 });
        int valueExp = 0;
        bool valueSign = false;

        using var resultSig = new GpuUIntBigInteger(ctx, 4);

        CSharpBigFloatArithmetic.Instance.Reciprocal(ref execCtx, 
            valueSig.Handle, valueExp, valueSign,
            resultSig.Handle, out int resultExp, out bool resultSign);

        var result = new 
        { 
            ResultWords = resultSig.ToHostArray(),
            ResultExp = resultExp,
            ResultSign = resultSign 
        };
        return Verify(result);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    public Task Test_GBF01_CpuReference_Normalize()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuContext(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 0x00000000_00000001
        using var valueSig = new GpuUIntBigInteger(ctx, new uint[] { 1, 0, 0, 0 });
        int valueExp = 0;
        bool valueSign = false;

        // Target: 4 words (128 bits). 1 is at bit 0. It should be shifted to bit 127.
        using var resultSig = new GpuUIntBigInteger(ctx, 4);

        CSharpBigFloatArithmetic.Instance.Normalize(ref execCtx, 
            valueSig.Handle, valueExp, valueSign,
            resultSig.Handle, out int resultExp, out bool resultSign);

        var result = new 
        { 
            ResultWords = resultSig.ToHostArray(),
            ResultExp = resultExp,
            ResultSign = resultSign 
        };
        return Verify(result);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    public Task Test_GBF01_GpuReference_Normalize()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuContext(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 0x00000000_00000001
        using var valueSig = new GpuUIntBigInteger(ctx, new uint[] { 1, 0, 0, 0 });
        int valueExp = 0;
        bool valueSign = false;

        // Target: 4 words (128 bits). 1 is at bit 0. It should be shifted to bit 127.
        using var resultSig = new GpuUIntBigInteger(ctx, 4);

        GSharpAotFloatProvider.Instance.Normalize(ref execCtx, 
            valueSig.Handle, valueExp, valueSign,
            resultSig.Handle, out int resultExp, out bool resultSign);

        var result = new 
        { 
            ResultWords = resultSig.ToHostArray(),
            ResultExp = resultExp,
            ResultSign = resultSign 
        };
        return Verify(result);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    public Task Test_GBF01_CpuReference_Sqrt()
    {
        CudaCapability.EnsureGpuAvailable();
        var result = new { Status = "Not Implemented Yet" };
        return Verify(result);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    public Task Test_GBF01_CpuReference_Exp()
    {
        CudaCapability.EnsureGpuAvailable();
        var result = new { Status = "Not Implemented Yet" };
        return Verify(result);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    public Task Test_GBF01_CpuReference_Log()
    {
        CudaCapability.EnsureGpuAvailable();
        var result = new { Status = "Not Implemented Yet" };
        return Verify(result);
    }

    protected VerifyTests.SettingsTask Verify(object? target, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "")
    {
        var prefixParts = methodName.Split('_');
        var prefix = prefixParts.Length > 1 ? prefixParts[1] : "Test";
        var hash10 = Helpers.HashHelper.ComputeShortHash(methodName);
        var snapshotName = $"Snap.{prefix}_{hash10}";
        return VerifyXunit.Verifier.Verify(target).UseFileName(snapshotName);
    }
}
