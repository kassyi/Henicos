#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

// [CudaImport] launchers next to parameter names and types that the generated body must not depend on. Each case has
// its own namespace, so that a type it declares shadows only its own launcher. No global using of the runtime: the
// generated part names every type it uses from global::.

namespace Collisions.DeviceLocal
{
    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] a, [In] float[] d_a);
    }
}

namespace Collisions.KernelLocal
{
    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] input, int kernel);
    }
}

namespace Collisions.ScopeLocal
{
    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] input, int scope);
    }
}

namespace Collisions.ContextLocal
{
    public static partial class Invoker
    {
        [CudaImport("module", PackContext = true)]
        public static partial void Run([In] float[] input, int ctx);
    }
}

namespace Collisions.ParamsLocal
{
    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] input, int kernelParams);
    }
}

namespace Collisions.ArgumentLocal
{
    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] input, int p_input, out int result, int h_result);
    }
}

namespace Collisions.IntPtrType
{
    public struct IntPtr;

    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run(float[]? input, float factor);
    }
}

namespace Collisions.LoaderType
{
    public struct GpuKernelLoader;

    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] input, float factor);
    }
}

namespace Collisions.BufferType
{
    public struct GpuBuffer<T>;

    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] input, ref float total);
    }
}

namespace Collisions.DriverTypes
{
    public struct CudaException;

    public struct CudaDriverApi;

    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] input, float factor);
    }
}

namespace Collisions.SystemTypes
{
    public class Lazy<T>;

    public class AppContext;

    public class System;

    public struct Span<T>;

    public struct ReadOnlySpan<T>;

    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] input, ref float total, float[,] grid, uint gridDimX);
    }
}

namespace Collisions.Control
{
    public static partial class Invoker
    {
        [CudaImport("module")]
        public static partial void Run([In] float[] input, float factor);
    }
}

// Two module names that differ only in a character that is not a letter or a digit, and overloads that pack their
// arguments: each module and each argument struct has a name of its own
namespace Collisions.Members
{
    public static partial class Invoker
    {
        [CudaImport("k.fatbin")]
        public static partial void Dotted([In] float[] input);

        [CudaImport("k_fatbin")]
        public static partial void Underscored([In] float[] input);

        [CudaImport("k.fatbin", PackContext = true)]
        public static partial void Packed([In] float[] input, int count);

        [CudaImport("k.fatbin", PackContext = true)]
        public static partial void Packed([In] float[] input, float scale);
    }
}
