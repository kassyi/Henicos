#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

// [CudaImport] declarations whose generated part must repeat the declaration: parameter modifiers, keyword names,
// nested, generic, struct and record containers, and classes of the same name in other namespaces or types

namespace Declarations.Modifiers
{
    public static partial class K
    {
        [CudaImport("m.fatbin")]
        public static partial void Run(ref float x, uint gridDimX);

        [CudaImport("m.fatbin", EntryPoint = "Run")]
        public static partial void Many(params float[] x);

        [CudaImport("m.fatbin", EntryPoint = "Run")]
        public static partial void Keywords([In] float[] @in, float[] @out, ref int @ref, out int @int);

        [CudaImport("m.fatbin", EntryPoint = "checked")]
        internal static partial void @checked(float[] @fixed, uint @uint);
    }

    public static unsafe partial class Pointers
    {
        [CudaImport("m.fatbin")]
        public static partial void Run(float* data, delegate* unmanaged<int, void> callback);
    }
}

namespace Declarations.Containers
{
    public static partial class Outer
    {
        public static partial class K
        {
            [CudaImport("m.fatbin")]
            public static partial void Run(float[] x);
        }
    }

    public partial class Generic<T>
    {
        [CudaImport("m.fatbin")]
        public static partial void Run(float[] x);

        public partial struct Inner<U>
            where U : struct
        {
            [CudaImport("m.fatbin")]
            public static partial void Run(float[] x);
        }
    }

    public partial struct Value
    {
        [CudaImport("m.fatbin")]
        public static partial void Run(float[] x);
    }

    public ref partial struct RefValue
    {
        [CudaImport("m.fatbin")]
        public static partial void Run(float[] x);
    }

    public partial record struct ValueRecord(int Id)
    {
        [CudaImport("m.fatbin")]
        public static partial void Run(float[] x);
    }

    public partial record Record(int Id)
    {
        [CudaImport("m.fatbin")]
        public static partial void Run(float[] x);
    }

    internal partial class @class
    {
        [CudaImport("m.fatbin")]
        public partial void Run(float[] x);
    }

    // A class of the same name as Outer.K in the same namespace
    public static partial class K
    {
        [CudaImport("other.fatbin")]
        public static partial void Run(float[] x);
    }
}

namespace Declarations.A
{
    public static partial class Kernels
    {
        [CudaImport("a.fatbin")]
        public static partial void Run(float[] x, uint gridDimX, uint blockDimX);
    }
}

namespace Declarations.B
{
    public static partial class Kernels
    {
        [CudaImport("b.fatbin")]
        public static partial void Run(float[] x, uint gridDimX, uint blockDimX);
    }
}
