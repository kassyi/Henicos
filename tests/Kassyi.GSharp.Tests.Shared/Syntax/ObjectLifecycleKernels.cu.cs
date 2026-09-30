// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct LifecycleObjectState
{
    public int Id;
    public int Value;
    public int Status;
}

public static partial class ObjectLifecycleKernels
{
    [GpuDeviceFunction]
    public static LifecycleObjectState TransformState(LifecycleObjectState state, int multiplier)
    {
        state.Value = state.Value * multiplier;
        state.Status = state.Value > 100 ? 1 : 0;
        return state;
    }

    [GpuKernel]
    public static void Kernel_LifecycleUpdate(LifecycleObjectState[] states, int length, int multiplier)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid < length)
        {
            LifecycleObjectState s = states[tid];
            states[tid] = TransformState(s, multiplier);
        }
    }

    [GpuKernel]
    public static void Kernel_FactoryInitialization(int[] inputValues, LifecycleObjectState[] outputStates, int length)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid < length)
        {
            LifecycleObjectState state = default;
            state.Id = tid + 1;
            state.Value = inputValues[tid];
            state.Status = inputValues[tid] < 0 ? -1 : (inputValues[tid] == 0 ? 0 : 1);
            outputStates[tid] = state;
        }
    }
}
