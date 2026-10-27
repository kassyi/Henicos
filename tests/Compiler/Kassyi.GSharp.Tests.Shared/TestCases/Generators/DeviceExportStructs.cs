using System;
using Kassyi.GSharp.Core;

// A library whose device code uses structs that it does not record itself (Kassyi.GSharp.Core.Float3), structs that a
// constructor names only in its signature, a constructor that needs an include, and a Dispose that only one of two
// functions writes
namespace DeviceExportStructsCase
{
    // A field of a Core vector type, which Kassyi.GSharp.Core declares and no G# assembly records
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

    // A generic struct that only a constructor of Wrapper names
    public struct Box<T>
    {
        public T Value;
    }

    // Exported on its own (a constructor with a body); its second constructor takes a Box<int>, which no device code of
    // the library uses
    public struct Wrapper
    {
        public int V;

        public Wrapper(int v)
        {
            V = v;
        }

        public Wrapper(Box<int> box)
        {
            V = box.Value;
        }
    }

    // A constructor that sorts: the CUDA text needs <thrust/sort.h>
    public struct SortedPair
    {
        public int Lo;
        public int Hi;

        public SortedPair(Span<int> values)
        {
            values.Sort();
            Lo = values[0];
            Hi = values[1];
        }
    }

    // A generic struct that only Holder names
    public struct Cell<T>
    {
        public T Value;
    }

    // Only its Dispose names Float3, a struct that no G# assembly records
    public struct Lease3 : IDisposable
    {
        public int V;

        public void Dispose()
        {
            var f = new Float3(1f, 2f, 3f);
            V = (int)f.X;
        }
    }

    // Its Dispose cannot be emitted (a throw): Holder's record fails, Cell<int>'s does not
    public struct Holder : IDisposable
    {
        public Cell<int> C;

        public Holder(int v)
        {
            C.Value = v;
        }

        public void Dispose()
        {
            throw new InvalidOperationException();
        }
    }

    public static partial class LibVector
    {
        // Fails in both dialects (a class instance); the struct it uses does not
        [GpuDeviceFunction]
        public static int Bad(int x)
        {
            var mark = new Mark();
            mark.V = x;
            object o = new object();
            return mark.V;
        }

        private struct Mark
        {
            public int V;
        }

        // A Float3 parameter: the text names the Core struct, whose definition the library does not record
        [GpuDeviceFunction]
        public static float Dot3(Float3 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;

        // A guard that only Use disposes: Peek's struct record comes first and has no Dispose text
        [GpuDeviceFunction]
        public static int Peek(int x)
        {
            var guard = new Guard();
            guard.V = x;
            return guard.V;
        }

        [GpuDeviceFunction]
        public static int Use(int x)
        {
            using var guard = new Guard();
            return x + guard.V;
        }

        private struct Guard : IDisposable
        {
            public int V;

            public void Dispose()
            {
                V = 0;
            }
        }
    }
}
