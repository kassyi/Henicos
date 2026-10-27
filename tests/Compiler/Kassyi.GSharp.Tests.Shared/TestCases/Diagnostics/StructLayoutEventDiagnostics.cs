using System;
using Kassyi.GSharp.Core;

namespace StructLayoutEventDiagnosticsTest
{
    // A field-like event stores its delegate in a hidden field (#621)
    public struct WithEvent { public int A; public event Action Changed; }

    // The event stores no delegate: add and remove are written out
    public struct WithAccessorEvent
    {
        public int A;
        public event Action Changed { add { } remove { } }
    }

    // GSH0078 at the parameter, the array and the local; the last kernel reports nothing
    public static partial class StructLayoutEventKernels
    {
        [GpuKernel]
        public static void EventByValueKernel(WithEvent w, int[] r) { r[0] = w.A; }

        [GpuKernel]
        public static void EventArrayKernel(WithEvent[] w, int[] r) { r[0] = w[1].A; }

        [GpuKernel]
        public static void EventLocalKernel(int[] r)
        {
            WithEvent local = default;
            r[0] = local.A;
        }

        [GpuKernel]
        public static void AccessorEventKernel(WithAccessorEvent w, WithAccessorEvent[] ws, int[] r) { r[0] = w.A + ws[0].A; }
    }

    // These events store nothing either: a static event, an extern event, and a partial event (C# 14) whose
    // implementing part writes add and remove
    public struct WithStaticEvent { public int A; public static event Action Shared; }

    public struct WithExternEvent { public int A; public extern event Action Native; }

    public partial struct WithPartialEvent { public int A; public partial event Action Changed; }

    public partial struct WithPartialEvent { public partial event Action Changed { add { } remove { } } }

    public static partial class StructLayoutStoragelessEventKernels
    {
        [GpuKernel]
        public static void StoragelessEventKernel(WithStaticEvent s, WithExternEvent x, WithPartialEvent p, int[] r)
        {
            r[0] = s.A + x.A + p.A;
        }
    }
}
