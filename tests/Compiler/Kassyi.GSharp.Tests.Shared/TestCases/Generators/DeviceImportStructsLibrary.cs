using System;
using Kassyi.GSharp.Core;

// A library whose structs depend on its device functions and on each other, for the order of the imported text
namespace DeviceImportStructsCase
{
    // A constructor that calls a device function of the library: the function is declared before the struct
    public struct Scaled
    {
        public int V;

        public Scaled(int v)
        {
            V = LibStructs.Twice(v);
        }
    }

    // A field of Omega, and a computed property that names Zeta only through its constructor; both come before Alpha,
    // which sorts first by name
    public struct Alpha
    {
        public Omega Inner;

        public Alpha(int v)
        {
            Inner = new Omega(v);
        }

        public int Probe => new Zeta(Inner.V).V;
    }

    public struct Omega
    {
        public int V;

        public Omega(int v)
        {
            V = v + 1;
        }
    }

    public struct Zeta
    {
        public int V;

        public Zeta(int v)
        {
            V = v * 3;
        }
    }

    // A generic struct with a constructor; the library uses Box<int> only
    public struct Box<T>
    {
        public T Value;

        public Box(T value)
        {
            Value = value;
        }
    }

    // Two constructors, _o0 and _o1 in the order of their declarations
    public struct Range2
    {
        public int Lo;
        public int Hi;

        public Range2(int both)
        {
            Lo = both;
            Hi = both;
        }

        public Range2(int lo, int hi)
        {
            Lo = lo;
            Hi = hi + 1;
        }
    }

    // A constructor that writes an array element
    public struct Recorder
    {
        public int V;

        public Recorder(int[] log, int v)
        {
            log[0] = v;
            V = v;
        }
    }

    // A Dispose that a function of the library calls
    public struct Lease : IDisposable
    {
        public int V;

        public void Dispose()
        {
            V = 0;
        }
    }

    // A field of a Core vector type
    public struct Particle
    {
        public Float3 Position;
        public float Mass;

        public Particle(Float3 position, float mass)
        {
            Position = position;
            Mass = mass * 2f;
        }

        public float Weight => Mass + Position.X;
    }

    public static partial class LibStructs
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;

        [GpuDeviceFunction]
        public static int Unbox(int x) => new Box<int>(x).Value;

        [GpuDeviceFunction]
        public static int Borrow(int x)
        {
            using var lease = new Lease();
            return x + lease.V;
        }

        // Relay suspends and calls Wait, which suspends: Relay's frame embeds Wait's, and Relay sorts first by name
        [GpuDeviceFunction]
        public static int Relay(int[] buffer, int tid)
        {
            int v = Wait(buffer, tid);
            Gpu.SyncThreads();
            return v;
        }

        [GpuDeviceFunction]
        public static int Wait(int[] buffer, int tid)
        {
            buffer[tid] = tid;
            Gpu.SyncThreads();
            return buffer[(tid + 1) % 4];
        }

        // Depends on parallel execution
        [GpuDeviceFunction]
        public static void GridTouch(int n)
        {
            Gpu.GridStrideLoop(n, i => { });
        }

        [GpuDeviceFunction]
        public static float Dot3(Float3 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;

        [GpuDeviceFunction]
        public static float Length3(float x, float y, float z) => Dot3(new Float3(x, y, z));
    }
}
