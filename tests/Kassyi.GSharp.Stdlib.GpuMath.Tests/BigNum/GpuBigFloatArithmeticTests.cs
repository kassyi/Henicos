#nullable enable
using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Xunit;
using VerifyXunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.GpuBigFloat;

public class GpuBigFloatArithmeticTests
{
    private GpuBigIntExecutionContext CreateContext(GpuWorkspace ctx)
    {
        return new GpuBigIntExecutionContext(ctx, ctx.MemoryManager, default!, ctx.Stream);
    }

    [SkippableFact]
    public Task Test_GBF01_CpuReference_Divide()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 100 * 2^10 / 3 * 2^5
        using var leftSig = new GpuBigUInt(ctx, new uint[] { 100, 0, 0, 0 });
        int leftExp = 10;
        bool leftSign = false;

        using var rightSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int rightExp = 5;
        bool rightSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Divide(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Divide()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 100 * 2^10 / 3 * 2^5
        using var leftSig = new GpuBigUInt(ctx, new uint[] { 100, 0, 0, 0 });
        int leftExp = 10;
        bool leftSign = false;

        using var rightSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int rightExp = 5;
        bool rightSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Divide(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Divide_Mega()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // Generate 2048 words (mega division test > 1024 fast path threshold)
        var random = new Random(42);
        var leftArray = new uint[2048];
        var rightArray = new uint[2048];
        for (int i = 0; i < 2048; i++)
        {
            leftArray[i] = unchecked((uint)random.Next());
            rightArray[i] = unchecked((uint)random.Next());
        }

        leftArray[^1] |= 0x80000000;
        rightArray[^1] |= 0x80000000;

        using var leftSig = new GpuBigUInt(ctx, leftArray);
        int leftExp = 10;
        bool leftSign = false;

        using var rightSig = new GpuBigUInt(ctx, rightArray);
        int rightExp = 5;
        bool rightSign = false;

        using var resultSig = new GpuBigUInt(ctx, 2048);

        CSharpCpuBigFloatArithmetic.Instance.Divide(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Divide_Mega()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // Generate 2048 words (mega division test > 1024 fast path threshold)
        var random = new Random(42);
        var leftArray = new uint[2048];
        var rightArray = new uint[2048];
        for (int i = 0; i < 2048; i++)
        {
            leftArray[i] = unchecked((uint)random.Next());
            rightArray[i] = unchecked((uint)random.Next());
        }

        leftArray[^1] |= 0x80000000;
        rightArray[^1] |= 0x80000000;

        using var leftSig = new GpuBigUInt(ctx, leftArray);
        int leftExp = 10;
        bool leftSign = false;

        using var rightSig = new GpuBigUInt(ctx, rightArray);
        int rightExp = 5;
        bool rightSign = false;

        using var resultSig = new GpuBigUInt(ctx, 2048);

        GSharpAotFloatProvider.CreateGSharpKernel().Divide(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Multiply()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 1.5 * 2^10 * 3.0 * 2^5
        using var
            leftSig = new GpuBigUInt(ctx,
                new uint[] { 3, 0, 0, 0 }); // Represents 3.0 conceptually (unnormalized here)
        int leftExp = 10;
        bool leftSign = false;

        using var rightSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int rightExp = 5;
        bool rightSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Multiply(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Multiply()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 1.5 * 2^10 * 3.0 * 2^5
        using var leftSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int leftExp = 10;
        bool leftSign = false;

        using var rightSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int rightExp = 5;
        bool rightSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Multiply(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Add()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 3.0 * 2^10 + 3.0 * 2^5
        using var leftSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int leftExp = 10;
        bool leftSign = false;

        using var rightSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int rightExp = 5;
        bool rightSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Add(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Add()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 3.0 * 2^10 + 3.0 * 2^5
        using var leftSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int leftExp = 10;
        bool leftSign = false;

        using var rightSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int rightExp = 5;
        bool rightSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Add(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Reciprocal()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 1 / (3 * 2^0)
        using var valueSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int valueExp = 0;
        bool valueSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Reciprocal(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Reciprocal()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 1 / (3 * 2^0)
        using var valueSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int valueExp = 0;
        bool valueSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Reciprocal(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Sqrt()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // Sqrt(9.0 * 2^10) = 3.0 * 2^5
        using var rawValueSig = new GpuBigUInt(ctx, new uint[] { 9, 0, 0, 0 });
        int rawValueExp = 10;
        bool rawValueSign = false;

        using var valueSig = new GpuBigUInt(ctx, 4);
        CSharpCpuBigFloatArithmetic.Instance.Normalize(ref execCtx,
            rawValueSig.Handle, rawValueExp, rawValueSign,
            valueSig.Handle, out int valueExp, out bool valueSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Sqrt(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Sqrt()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        using var rawValueSig = new GpuBigUInt(ctx, new uint[] { 9, 0, 0, 0 });
        int rawValueExp = 10;
        bool rawValueSign = false;

        using var valueSig = new GpuBigUInt(ctx, 4);
        GSharpAotFloatProvider.CreateGSharpKernel().Normalize(ref execCtx,
            rawValueSig.Handle, rawValueExp, rawValueSign,
            valueSig.Handle, out int valueExp, out bool valueSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Sqrt(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Rsqrt()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // Rsqrt(9.0 * 2^10) = 1 / (3.0 * 2^5)
        using var rawValueSig = new GpuBigUInt(ctx, new uint[] { 9, 0, 0, 0 });
        int rawValueExp = 10;
        bool rawValueSign = false;

        using var valueSig = new GpuBigUInt(ctx, 4);
        CSharpCpuBigFloatArithmetic.Instance.Normalize(ref execCtx,
            rawValueSig.Handle, rawValueExp, rawValueSign,
            valueSig.Handle, out int valueExp, out bool valueSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Rsqrt(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Rsqrt()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        using var rawValueSig = new GpuBigUInt(ctx, new uint[] { 9, 0, 0, 0 });
        int rawValueExp = 10;
        bool rawValueSign = false;

        using var valueSig = new GpuBigUInt(ctx, 4);
        GSharpAotFloatProvider.CreateGSharpKernel().Normalize(ref execCtx,
            rawValueSig.Handle, rawValueExp, rawValueSign,
            valueSig.Handle, out int valueExp, out bool valueSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Rsqrt(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Normalize()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 0x00000000_00000001
        using var valueSig = new GpuBigUInt(ctx, new uint[] { 1, 0, 0, 0 });
        int valueExp = 0;
        bool valueSign = false;

        // Target: 4 words (128 bits). 1 is at bit 0. It should be shifted to bit 127.
        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Normalize(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Normalize()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 0x00000000_00000001
        using var valueSig = new GpuBigUInt(ctx, new uint[] { 1, 0, 0, 0 });
        int valueExp = 0;
        bool valueSign = false;

        // Target: 4 words (128 bits). 1 is at bit 0. It should be shifted to bit 127.
        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Normalize(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Exp()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // Exp(1.0 * 2^0) = e
        using var rawValueSig = new GpuBigUInt(ctx, new uint[] { 1, 0, 0, 0 });
        int rawValueExp = 0;
        bool rawValueSign = false;

        using var valueSig = new GpuBigUInt(ctx, 4);
        CSharpCpuBigFloatArithmetic.Instance.Normalize(ref execCtx,
            rawValueSig.Handle, rawValueExp, rawValueSign,
            valueSig.Handle, out int valueExp, out bool valueSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Exp(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Exp()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        using var rawValueSig = new GpuBigUInt(ctx, new uint[] { 1, 0, 0, 0 });
        int rawValueExp = 0;
        bool rawValueSign = false;

        using var valueSig = new GpuBigUInt(ctx, 4);
        GSharpAotFloatProvider.CreateGSharpKernel().Normalize(ref execCtx,
            rawValueSig.Handle, rawValueExp, rawValueSign,
            valueSig.Handle, out int valueExp, out bool valueSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Exp(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Log()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // Log(2.0 * 2^0) = ln(2)
        using var rawValueSig = new GpuBigUInt(ctx, new uint[] { 2, 0, 0, 0 });
        int rawValueExp = 0;
        bool rawValueSign = false;

        using var valueSig = new GpuBigUInt(ctx, 4);
        CSharpCpuBigFloatArithmetic.Instance.Normalize(ref execCtx,
            rawValueSig.Handle, rawValueExp, rawValueSign,
            valueSig.Handle, out int valueExp, out bool valueSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Log(ref execCtx,
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
    public Task Test_GBF01_GpuReference_Log()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        using var rawValueSig = new GpuBigUInt(ctx, new uint[] { 2, 0, 0, 0 });
        int rawValueExp = 0;
        bool rawValueSign = false;

        using var valueSig = new GpuBigUInt(ctx, 4);
        GSharpAotFloatProvider.CreateGSharpKernel().Normalize(ref execCtx,
            rawValueSig.Handle, rawValueExp, rawValueSign,
            valueSig.Handle, out int valueExp, out bool valueSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Log(ref execCtx,
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
    public Task Test_GBF01_CpuReference_Pow()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // Pow(2.0, 3.0) = 8.0
        using var rawBaseSig = new GpuBigUInt(ctx, new uint[] { 2, 0, 0, 0 });
        int rawBaseExp = 0;
        bool rawBaseSign = false;
        using var baseSig = new GpuBigUInt(ctx, 4);
        CSharpCpuBigFloatArithmetic.Instance.Normalize(ref execCtx, rawBaseSig.Handle, rawBaseExp, rawBaseSign,
            baseSig.Handle, out int baseExp, out bool baseSign);

        using var rawExpSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int rawExpExp = 0;
        bool rawExpSign = false;
        using var expSig = new GpuBigUInt(ctx, 4);
        CSharpCpuBigFloatArithmetic.Instance.Normalize(ref execCtx, rawExpSig.Handle, rawExpExp, rawExpSign,
            expSig.Handle,
            out int expExp, out bool expSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.Pow(ref execCtx,
            baseSig.Handle, baseExp, baseSign,
            expSig.Handle, expExp, expSign,
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
    public Task Test_GBF01_GpuReference_Pow()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        using var rawBaseSig = new GpuBigUInt(ctx, new uint[] { 2, 0, 0, 0 });
        int rawBaseExp = 0;
        bool rawBaseSign = false;
        using var baseSig = new GpuBigUInt(ctx, 4);
        GSharpAotFloatProvider.CreateGSharpKernel().Normalize(ref execCtx, rawBaseSig.Handle, rawBaseExp, rawBaseSign,
            baseSig.Handle, out int baseExp, out bool baseSign);

        using var rawExpSig = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        int rawExpExp = 0;
        bool rawExpSign = false;
        using var expSig = new GpuBigUInt(ctx, 4);
        GSharpAotFloatProvider.CreateGSharpKernel().Normalize(ref execCtx, rawExpSig.Handle, rawExpExp, rawExpSign,
            expSig.Handle,
            out int expExp, out bool expSign);

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().Pow(ref execCtx,
            baseSig.Handle, baseExp, baseSign,
            expSig.Handle, expExp, expSign,
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
    public Task Test_GBF01_CpuReference_DivideByUInt32_Basic()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 1000 / 3
        using var valueSig = new GpuBigUInt(ctx, new uint[] { 1000, 0, 0, 0 });
        int valueExp = 0;
        bool valueSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.DivideByUInt32(ref execCtx,
            valueSig.Handle, valueExp, valueSign,
            3,
            resultSig.Handle, out int resultExp, out bool resultSign, out uint remainder);

        var result = new
        {
            ResultWords = resultSig.ToHostArray(),
            ResultExp = resultExp,
            ResultSign = resultSign,
            Remainder = remainder
        };
        return Verify(result);
    }

    [SkippableFact]
    public Task Test_GBF01_GpuReference_DivideByUInt32_Basic()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 1000 / 3
        using var valueSig = new GpuBigUInt(ctx, new uint[] { 1000, 0, 0, 0 });
        int valueExp = 0;
        bool valueSign = false;

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().DivideByUInt32(ref execCtx,
            valueSig.Handle, valueExp, valueSign,
            3,
            resultSig.Handle, out int resultExp, out bool resultSign, out uint remainder);

        var result = new
        {
            ResultWords = resultSig.ToHostArray(),
            ResultExp = resultExp,
            ResultSign = resultSign,
            Remainder = remainder
        };
        return Verify(result);
    }

    [SkippableFact]
    public Task Test_GBF01_CpuReference_DivideByUInt32_Boundary()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 0xFFFFFFFF_FFFFFFFF_FFFFFFFF_FFFFFFFF / (2^32 - 1)
        using var valueSig = new GpuBigUInt(ctx, new uint[] { 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF });
        int valueExp = 5;
        bool valueSign = true; // Negative

        using var resultSig = new GpuBigUInt(ctx, 4);

        CSharpCpuBigFloatArithmetic.Instance.DivideByUInt32(ref execCtx,
            valueSig.Handle, valueExp, valueSign,
            0xFFFFFFFF, // Max uint
            resultSig.Handle, out int resultExp, out bool resultSign, out uint remainder);

        var result = new
        {
            ResultWords = resultSig.ToHostArray(),
            ResultExp = resultExp,
            ResultSign = resultSign,
            Remainder = remainder
        };
        return Verify(result);
    }

    [SkippableFact]
    public Task Test_GBF01_GpuReference_DivideByUInt32_Boundary()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var execCtx = CreateContext(ctx);

        // 0xFFFFFFFF_FFFFFFFF_FFFFFFFF_FFFFFFFF / (2^32 - 1)
        using var valueSig = new GpuBigUInt(ctx, new uint[] { 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF });
        int valueExp = 5;
        bool valueSign = true;

        using var resultSig = new GpuBigUInt(ctx, 4);

        GSharpAotFloatProvider.CreateGSharpKernel().DivideByUInt32(ref execCtx,
            valueSig.Handle, valueExp, valueSign,
            0xFFFFFFFF, // Max uint
            resultSig.Handle, out int resultExp, out bool resultSign, out uint remainder);

        var result = new
        {
            ResultWords = resultSig.ToHostArray(),
            ResultExp = resultExp,
            ResultSign = resultSign,
            Remainder = remainder
        };
        return Verify(result);
    }

    [SkippableFact]
    public void Test_GBF01_CpuReference_DivideByUInt32_Zero()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        using var valueSig = new GpuBigUInt(ctx, new uint[] { 1000, 0, 0, 0 });
        using var resultSig = new GpuBigUInt(ctx, 4);

        Assert.Throws<DivideByZeroException>(() =>
        {
            var execCtx = CreateContext(ctx);
            CSharpCpuBigFloatArithmetic.Instance.DivideByUInt32(ref execCtx,
                valueSig.Handle, 0, false,
                0, // Divide by zero
                resultSig.Handle, out int resultExp, out bool resultSign, out uint remainder);
        });
    }

    [SkippableFact]
    public void Test_GBF01_GpuReference_DivideByUInt32_Zero()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        using var valueSig = new GpuBigUInt(ctx, new uint[] { 1000, 0, 0, 0 });
        using var resultSig = new GpuBigUInt(ctx, 4);

        Assert.Throws<InvalidOperationException>(() =>
        {
            var execCtx = CreateContext(ctx);
            GSharpAotFloatProvider.CreateGSharpKernel().DivideByUInt32(ref execCtx,
                valueSig.Handle, 0, false,
                0, // Divide by zero
                resultSig.Handle, out int resultExp, out bool resultSign, out uint remainder);
        });
    }

    protected Task Verify(object? target,
        [CallerMemberName] string methodName = "")
    {
        return Verifier.Verify(target).UseFastHashSnapshotName(methodName);
    }
}
