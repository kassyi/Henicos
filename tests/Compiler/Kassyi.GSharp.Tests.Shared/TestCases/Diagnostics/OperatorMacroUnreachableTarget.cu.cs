using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class ReachHolder
{
    // A private struct: no code outside ReachHolder can name it, and [UnsafeAccessorType] does not take value types
    private struct Hidden
    {
        [GpuDeviceFunction]
        private static void Add(ref ReachValue result, ReachValue left, ReachValue right)
        {
            result.V = left.V + right.V;
        }
    }

    // A private static class: the managed path reaches its private target through [UnsafeAccessorType]
    private static partial class HiddenOps
    {
        [GpuDeviceFunction]
        private static void Sub(ref ReachValue result, ReachValue left, ReachValue right)
        {
            result.V = left.V - right.V;
        }
    }

    public struct ReachValue
    {
        public int V;

        [GpuOperatorMacro(typeof(Hidden), "Add")]
        public static ReachValue operator +(ReachValue left, ReachValue right) => throw new InvalidOperationException();

        [GpuOperatorMacro(typeof(HiddenOps), "Sub")]
        public static ReachValue operator -(ReachValue left, ReachValue right) => throw new InvalidOperationException();
    }
}

public static partial class OperatorMacroUnreachableTargetKernels
{
    // GSH0003 on the C++ dialect, whose managed fallback cannot call ReachHolder.Hidden.Add from here, also as an inner
    // operator macro under '-'; nothing for '-' itself
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UnreachableKernel(int[] output)
    {
        ReachHolder.ReachValue a = default;
        ReachHolder.ReachValue c = default;
        c = a + a;
        c = a - a;
        output[0] = c.V;
        c = a - (a + a);
        output[1] = c.V;
    }
}
