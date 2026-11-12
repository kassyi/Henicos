using System;
using Kassyi.GSharp.Core;

namespace GpuMacroCallFormsTest
{
    public static partial class GpuMacroCallFormKernels
    {
        // A macro outside a statement (the body of an expression-bodied local function): GSH0003 at the call
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void ExpressionPositionKernel(int[] r)
        {
            void SingleThreadBody() => Gpu.SingleThread(() => r[0] = 5);
            void ForUnrollBody() => Gpu.ForUnroll(0, 2, i => r[i] = i);
            void GridStrideBody() => Gpu.GridStrideLoop(4, i => r[i] = i);
            void UnrollHint() => Gpu.Hint.Unroll();
            SingleThreadBody();
            ForUnrollBody();
            GridStrideBody();
            UnrollHint();
        }

        [GpuKernel(Cooperative = true)]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void SyncGridExpressionKernel(int[] r)
        {
            void SyncBody() => Gpu.SyncGrid(() => r[0] = 1);
            SyncBody();
        }

        // A macro body that is a method group: GSH0003 at the body
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void MethodGroupBodyKernel(int[] r)
        {
            Gpu.SingleThread(Body);
            Gpu.ForUnroll(0, 2, Each);
            Gpu.GridStrideLoop(4, Each);
        }

        [GpuDeviceFunction]
        public static void Body()
        {
        }

        [GpuDeviceFunction]
        public static void Each(int i)
        {
        }

        // Statements with lambda bodies: the macro rules write them
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void StatementKernel(int[] r)
        {
            Gpu.SingleThread(() => r[0] = 5);
            Gpu.ForUnroll(0, 2, i => r[i] = i);
            Gpu.GridStrideLoop(4, i => r[i] = i);
        }
    }
}
