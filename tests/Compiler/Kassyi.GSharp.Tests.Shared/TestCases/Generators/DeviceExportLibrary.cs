using System.Collections.Generic;
using Kassyi.GSharp.Core;

// A library without kernels: every device function and the structs that device code uses, or that a consumer could not
// define from metadata, are recorded for other assemblies (GpuDeviceExportAttribute)
namespace DeviceExportCase
{
    // A constructor and a computed property, used by no device code: recorded on its own
    public struct Vec2
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Sum => X + Y;
    }

    // Fields only, used by no device code: a consumer defines it from metadata, so it is not recorded
    public struct Plain
    {
        public int A;
        public int B;
    }

    // Fields only, used by a device function: recorded with the function's struct
    public struct UsedOnly
    {
        public int V;
    }

    public static partial class LibMath
    {
        [GpuDeviceFunction]
        public static uint Mu(uint n0)
        {
            uint inv = n0;
            for (int i = 0; i < 4; i++)
            {
                inv *= 2 - n0 * inv;
            }

            return 0u - inv;
        }

        [GpuDeviceFunction]
        public static int Twice(int x) => Helper(x) * 2;

        [GpuDeviceFunction]
        internal static int Helper(int x) => x;

        [GpuDeviceFunction]
        public static int Exchange(int[] buffer, int tid, int value)
        {
            buffer[tid] = value;
            Gpu.SyncThreads();
            int next = buffer[(tid + 1) % 4];
            Gpu.SyncThreads();
            return next;
        }

        // B7: the barrier diverges when the second argument does (DivergentConditionParameters = 0b10)
        [GpuDeviceFunction]
        public static void SyncIfEven(int[] buffer, int value)
        {
            if (value % 2 == 0)
            {
                Gpu.SyncThreads();
            }

            Gpu.SyncThreads();
        }

        // B7: some threads may leave before the barrier even with uniform arguments (HasExitOnlyBarrier)
        [GpuDeviceFunction]
        public static void SyncWithin(int count)
        {
            if (ThreadIndex.X >= count)
            {
                return;
            }

            Gpu.SyncThreads();
        }

        // A ref parameter, and a write to an array: a caller must treat the call as writing memory
        [GpuDeviceFunction]
        public static void WriteThrough(ref int target, int[] data)
        {
            data[0] = target;
            target = 1;
        }

        [GpuDeviceFunction]
        public static void Bump(ref int target) => target += 1;

        // Returns through a ref parameter and clears an array element: a caller must treat the call as writing memory
        [GpuDeviceFunction]
        public static int ReadAndClear(ref int target, int[] data)
        {
            data[0] = 0;
            return target;
        }

        // Writes only through its ref parameter
        [GpuDeviceFunction]
        public static int TakeOne(ref int target)
        {
            target -= 1;
            return target;
        }

        // Overloads: the C++ names carry the declaration order (_o0, _o1), which metadata does not have
        [GpuDeviceFunction]
        public static int Scale(int x) => x * 3;

        [GpuDeviceFunction]
        public static float Scale(float x) => x * 0.5f;

        [GpuDeviceFunction]
        public static int Scratch(int n, int value)
        {
            var scratch = new SharedMemory<int>(n);
            scratch[ThreadIndex.X] = value;
            return scratch[ThreadIndex.X];
        }

        [GpuDeviceFunction]
        public static int ReadUsed(UsedOnly used) => used.V;

        // An iterator is expanded where it is enumerated, so it is not recorded
        [GpuDeviceFunction]
        public static IEnumerable<int> Upto(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return i;
            }
        }
    }
}
