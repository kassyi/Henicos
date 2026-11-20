using Kassyi.GSharp.Core;

// A kernel without the pragma reaches a default interface member through a type parameter: the walk cannot name what
// it runs, so no function is listed, only the marked kernel
namespace SpillSuppressionDefaultMember
{
    public interface IDefaulted
    {
        int Value => Kernels.ViaDefaultMember(2);
    }

    public struct UsesDefault : IDefaulted
    {
        public int A;
    }

    public struct ReadsDefault<T> where T : struct, IDefaulted
    {
        public int N;

        public ReadsDefault(int n)
        {
            N = default(T).Value + n;
        }
    }

    public static partial class Kernels
    {
        [GpuDeviceFunction]
        public static int ViaDefaultMember(int x) => x + 1;

        // Reached only from the marked kernel
        [GpuDeviceFunction]
        public static int OnlyMarked(int x) => x + 2;

#pragma warning disable GS0001
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void MarkedKernel(int[] d)
        {
            d[0] = ViaDefaultMember(d[1]) + OnlyMarked(d[1]);
        }
#pragma warning restore GS0001

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedKernel(int[] d)
        {
            d[0] = new ReadsDefault<UsesDefault>(d[1]).N;
        }
    }
}
