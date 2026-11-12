using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

// The namespace of these kernel classes declares types named like the BCL and runtime types and namespaces that
// generated C# names, and a type named var: the generated parts reopen this namespace, so every name they write
// without global:: binds to these types instead, and every var declares a local of the struct var
namespace ShadowingScope
{
    public struct var { }

    public static class System { }

    public static class Kassyi { }

    public static class Microsoft { }

    public static class CudaDriverApi { }

    public static class CudaException { }

    public static class GpuKernelLoader { }

    public static class LaunchErrorRecords { }

    public static class LaunchLimits { }

    public static class ModuleKernelArguments { }

    public struct GSharpArrayProxy { }

    public struct IntPtr { }

    public struct UIntPtr { }

    public static class Unsafe { }

    public struct GCHandle { }

    public enum GCHandleType { }

    public static class Marshal { }

    public static class MemoryMarshal { }

    public struct Span<T> { }

    public struct ReadOnlySpan<T> { }

    public sealed class List<T> { }

    public static class ArrayPool<T> { }

    public static class Math { }

    public static class MathF { }

    public static class Array { }

    public static class Buffer { }

    public static class Interlocked { }

    public static class Volatile { }

    public sealed class Exception { }

    public sealed class ArgumentNullException { }

    public sealed class ArgumentException { }

    public sealed class ArgumentOutOfRangeException { }

    public sealed class InvalidOperationException { }

    public sealed class NotSupportedException { }

    public delegate void Action();

    public delegate void Func();

    public static class Console { }

    public static class Environment { }

    public static class Path { }

    public static class AppContext { }

    public static class RuntimeInformation { }

    public static class Architecture { }

    public static class OperatingSystem { }

    public static class Thread { }

    public static class Task { }

    public static class Parallel { }

    public static class GC { }

    public static class Object { }

    public static class String { }

    public static class Int32 { }

    public static class UInt32 { }

    public static class Int64 { }

    public static class Single { }

    public static class Double { }

    public static class Boolean { }

    public static class Byte { }

    public static class Activator { }

    public static class Type { }

    public static class Nullable { }

    public static class Memory<T> { }

    public static class Vector2 { }

    public static class Vector3 { }

    public static class Vector4 { }

    public sealed class StructLayoutAttribute { }

    public sealed class FieldOffsetAttribute { }

    public sealed class MethodImplAttribute { }

    public sealed class UnmanagedCallersOnlyAttribute { }

    public sealed class UnmanagedFunctionPointerAttribute { }

    public sealed class EditorBrowsableAttribute { }

    public sealed class GeneratedCodeAttribute { }

    public sealed class ObsoleteAttribute { }

    public sealed class FlagsAttribute { }

    public struct Payload
    {
        public int[] Values;
        public int[]? Optional;
        public int Count;
    }

    public struct Resource : IDisposable
    {
        public int Tag;

        public void Dispose()
        {
        }
    }

    public struct Pair
    {
        public int A;
        public float B;
    }

    public static partial class ShadowedKernels
    {
        [GpuKernel]
        public static void FillKernel(int[] data, float[]? optional, int count)
        {
            int i = ThreadIndex.X;
            if (i < count)
            {
                data[i] = optional == null ? i : (int)optional[i];
            }
        }

        // A parameter named nameof: an argument check written as nameof(nameof) would call the parameter
        [GpuKernel]
        public static void NameofKernel(int[] nameof) => nameof[ThreadIndex.X] = 1;

        [GpuKernel]
        public static void PayloadKernel(Payload payload, ref int total)
        {
            payload.Values[ThreadIndex.X] = payload.Count;
            if (ThreadIndex.X == 0)
            {
                total = payload.Count;
            }
        }

        [GpuKernel]
        public static Pair ResultKernel(int seed)
        {
            Pair p;
            p.A = seed + ThreadIndex.X;
            p.B = seed * 0.5f;
            return p;
        }

        [GpuKernel]
        public static void SharedKernel(int[] data)
        {
            SharedMemory<int> shared = new SharedMemory<int>(64);
            shared[ThreadIndex.X] = data[ThreadIndex.X];
            Gpu.SyncThreads();
            data[ThreadIndex.X] = shared[63 - ThreadIndex.X];
            Wait(data);
        }

        [GpuKernel(Cooperative = true)]
        public static void GridKernel(int[] data)
        {
            int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            data[tid] = tid;
            Gpu.SyncGrid();
            data[tid] += data[0];
        }

        [GpuKernel]
        public static void IteratorKernel(float[] data)
        {
            foreach (float v in Twice(data[ThreadIndex.X]))
            {
                data[ThreadIndex.X] += v;
            }
        }

        // A switch with a barrier in its sections: the flattened switch keeps the governing value in a temporary
        [GpuKernel]
        public static void SwitchKernel(int[] data, int mode)
        {
            switch (mode + data[0])
            {
                case 0:
                    data[ThreadIndex.X] = 1;
                    Gpu.SyncThreads();
                    break;
                default:
                    data[ThreadIndex.X] = 2;
                    Gpu.SyncThreads();
                    break;
            }
        }

        // A warp shuffle spills its arguments into temporaries
        [GpuKernel]
        public static void ShuffleKernel(int[] data)
        {
            int value = Warp.ShuffleDownSync(0xFFFFFFFFu, data[ThreadIndex.X] + 1, 1u);
            data[ThreadIndex.X] = value;
        }

        // using (<variable>) copies the variable into a hidden resource local
        [GpuKernel]
        public static void UsingKernel(int[] data)
        {
            Resource resource = new Resource { Tag = data[0] };
            using (resource)
            {
                data[ThreadIndex.X] = resource.Tag;
            }
        }

        // A stackalloc that lives across a barrier is cleared again on resume
        [GpuKernel]
        public static unsafe void StackKernel(int[] data)
        {
            int* s = stackalloc int[4];
            s[0] = data[ThreadIndex.X];
            Gpu.SyncThreads();
            data[ThreadIndex.X] = s[0];
        }

        // A foreach over an array and a condition that suspends: the source array and the left operand are kept in
        // temporaries
        [GpuKernel]
        public static void LoopKernel(int[] data, int[] values)
        {
            int sum = 0;
            foreach (int v in values)
            {
                Gpu.SyncThreads();
                sum += v;
            }

            if (sum > 0 && Ready(data))
            {
                data[ThreadIndex.X] = sum;
            }

            int picked = Pick(data) ?? 0;
            data[ThreadIndex.X] += picked;
        }

        [GpuDeviceFunction]
        public static bool Ready(int[] data)
        {
            Gpu.SyncThreads();
            return data[0] > 0;
        }

        [GpuDeviceFunction]
        public static int? Pick(int[] data)
        {
            Gpu.SyncThreads();
            return data[0] > 0 ? data[0] : null;
        }

        [GpuDeviceFunction]
        public static void Wait(int[] data)
        {
            Gpu.SyncThreads();
            data[ThreadIndex.X] += 1;
        }

        [GpuDeviceFunction]
        public static IEnumerable<float> Twice(float v)
        {
            yield return v;
            yield return v * 2f;
        }
    }

    public static partial class ShadowedImports
    {
        [CudaImport("shadow_module", EntryPoint = "Fill")]
        public static partial void Fill(int[] nameof, float[]? optional, int count, uint gridDimX, uint blockDimX);

        [CudaImport("shadow_module", EntryPoint = "Packed", PackContext = true)]
        public static partial void Packed(int[] data, double scale);
    }
}
