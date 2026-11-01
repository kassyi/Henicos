// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct OperatorMacroContextState
{
    public uint Offset;
    public uint Calls;
}

public static partial class OperatorMacroContextOps
{
    // Takes the context by reference: the caller sees the call count
    [GpuDeviceFunction]
    public static void AddWithOffset(ref OperatorMacroContextState ctx, uint[] result, uint[] left, uint[] right)
    {
        result[0] = left[0] + right[0] + ctx.Offset;
        ctx.Calls = ctx.Calls + 1u;
    }

    // Takes the context by value
    [GpuDeviceFunction]
    public static void ShiftWithOffset(OperatorMacroContextState ctx, uint[] result, uint[] operand, int shift)
    {
        result[0] = (operand[0] << shift) + ctx.Offset;
    }
}

public readonly ref struct OperatorMacroContextValue
{
    public readonly uint[] Data;

    public OperatorMacroContextValue(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(OperatorMacroContextOps), nameof(OperatorMacroContextOps.AddWithOffset))]
    public static OperatorMacroContextValue operator +(OperatorMacroContextValue a, OperatorMacroContextValue b) =>
        throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(OperatorMacroContextOps), nameof(OperatorMacroContextOps.ShiftWithOffset))]
    public static OperatorMacroContextValue operator <<(OperatorMacroContextValue a, int shift) =>
        throw new GpuOnlyException();

    public static implicit operator OperatorMacroContextValue(uint[] data) => new OperatorMacroContextValue(data);
    public static implicit operator uint[](OperatorMacroContextValue value) => value.Data;
}

public static partial class OperatorMacroContextKernels
{
    // #477: the kernel's context parameter is not named like the target's parameter
    [GpuKernel]
    public static void ParameterContextKernel(OperatorMacroContextState bigCtx, uint[] left, uint[] right,
        uint[] result, uint[] calls)
    {
        OperatorMacroContextValue a = left;
        OperatorMacroContextValue b = right;
        OperatorMacroContextValue c = result;

        c = a + b;
        calls[0] = bigCtx.Calls;
    }

    [GpuKernel]
    public static void ValueContextKernel(OperatorMacroContextState bigCtx, uint[] operand, uint[] result)
    {
        OperatorMacroContextValue a = operand;
        OperatorMacroContextValue c = result;

        c = a << 3;
    }

    // The context is a ref parameter of a device function
    [GpuDeviceFunction]
    public static void AddTwice(ref OperatorMacroContextState state, uint[] left, uint[] right, uint[] result)
    {
        OperatorMacroContextValue a = left;
        OperatorMacroContextValue b = right;
        OperatorMacroContextValue c = result;

        c = a + b;
        c = c + b;
    }

    [GpuKernel]
    public static void RefParameterContextKernel(OperatorMacroContextState bigCtx, uint[] left, uint[] right,
        uint[] result, uint[] calls)
    {
        AddTwice(ref bigCtx, left, right, result);
        calls[0] = bigCtx.Calls;
    }

    // The context is a local that lives across a barrier, so the C++ dialects keep it in the state machine frame
    [GpuKernel]
    public static void HoistedContextKernel(uint[] offset, uint[] left, uint[] right, uint[] result, uint[] calls)
    {
        OperatorMacroContextState local = default;
        local.Offset = offset[0];
        Gpu.SyncThreads();

        OperatorMacroContextValue a = left;
        OperatorMacroContextValue b = right;
        OperatorMacroContextValue c = result;

        c = a + b;
        Gpu.SyncThreads();
        calls[0] = local.Calls;
    }

    // The context is a value parameter of a suspending device function or a kernel that an operator macro changes
    // before a barrier (#944)
    [GpuDeviceFunction]
    public static uint AddAcrossBarrierWithValueContext(OperatorMacroContextState state, uint[] left, uint[] right,
        uint[] result)
    {
        OperatorMacroContextValue a = left;
        OperatorMacroContextValue b = right;
        OperatorMacroContextValue c = result;

        c = a + b;
        Gpu.SyncThreads();

        OperatorMacroContextValue bAfter = right;
        OperatorMacroContextValue cAfter = result;
        cAfter = cAfter + bAfter;
        return state.Calls;
    }

    [GpuKernel]
    public static void ValueParameterContextAcrossBarrierKernel(OperatorMacroContextState bigCtx, uint[] left,
        uint[] right, uint[] result, uint[] deviceCalls, uint[] kernelCalls)
    {
        deviceCalls[0] = AddAcrossBarrierWithValueContext(bigCtx, left, right, result);

        OperatorMacroContextValue a = result;
        OperatorMacroContextValue b = right;
        OperatorMacroContextValue c = result;

        c = a + b;
        Gpu.SyncThreads();

        OperatorMacroContextValue bAfter = right;
        OperatorMacroContextValue cAfter = result;
        cAfter = cAfter + bAfter;
        kernelCalls[0] = bigCtx.Calls;
    }
}
