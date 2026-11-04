#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;
using SMemInt = Kassyi.GSharp.Core.SharedMemory<int>;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.PendingBugs;

// #543: Generic struct constructors, operators, and computed properties allocating SharedMemory<T> (or via type alias):
// - Specializations `<int>` and `<float>` collide on the same static shared memory site key in constructors and operators.
// - Computed property getter allocating SharedMemory<T> fails in SharedMemorySiteKey.Compute because it is not a BaseMethodDeclarationSyntax.
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class GenericStructSharedMemorySiteKeyKernels
{
    public struct AliasInStruct<T> where T : unmanaged
    {
        public int Value;

        public AliasInStruct(int seed)
        {
            var s = new SMemInt(4);
            s[0] = seed;
            Value = s[0];
        }
    }

    public struct GenericCtorAndOpBuf<T> where T : unmanaged
    {
        public T Item;

        public GenericCtorAndOpBuf(T value)
        {
            var s = new SharedMemory<T>(4);
            s[0] = value;
            Item = s[0];
        }

        public static T operator +(GenericCtorAndOpBuf<T> a, GenericCtorAndOpBuf<T> b)
        {
            var s = new SharedMemory<T>(4);
            s[0] = a.Item;
            return s[0];
        }
    }

    public struct ComputedPropSharedMemoryBuf<T> where T : unmanaged
    {
        public T Fallback;

        public T FirstShared
        {
            get
            {
                var s = new SharedMemory<T>(4);
                return s[0];
            }
        }
    }

    [GpuKernel]
    public static void UseAliasInGenericStructKernel(int[] output)
    {
        var a = new AliasInStruct<int>(10);
        var b = new AliasInStruct<float>(20);
        output[0] = a.Value + b.Value;
    }

    [GpuKernel]
    public static void UseGenericCtorAndOpBufKernel(int[] intOut, float[] floatOut)
    {
        var bi1 = new GenericCtorAndOpBuf<int>(11);
        var bi2 = new GenericCtorAndOpBuf<int>(22);
        intOut[0] = bi1 + bi2;

        var bf1 = new GenericCtorAndOpBuf<float>(1.5f);
        var bf2 = new GenericCtorAndOpBuf<float>(2.5f);
        floatOut[0] = bf1 + bf2;
    }

    [GpuKernel]
    public static void UseComputedPropSharedMemoryBufKernel(int[] output)
    {
        var b = new ComputedPropSharedMemoryBuf<int> { Fallback = 1 };
        output[0] = b.FirstShared;
    }
}
