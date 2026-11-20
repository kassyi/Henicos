using System;
using Kassyi.GSharp.Core;

// A using of a value of a type parameter constrained to IDisposable runs the Dispose of the type argument. The CUDA
// module does not define that Dispose yet, so the tests read the call graph of the walk, not the module
namespace SpillSuppressionTypeParameterDispose
{
    public struct Resource : IDisposable
    {
        public int A;

        public void Dispose()
        {
            A = Kernels.ViaTypeParameterDispose(A);
        }
    }

    public struct UsingLocal<T> where T : struct, IDisposable
    {
        public int N;

        public UsingLocal(int n)
        {
            using (var resource = new T())
            {
                N = n;
            }
        }
    }

    public struct UsingDeclaration<T> where T : struct, IDisposable
    {
        public int N;

        public UsingDeclaration(int n)
        {
            using var resource = new T();
            N = n;
        }
    }

    public struct UsingResource<T> where T : struct, IDisposable
    {
        public int N;

        public UsingResource(int n)
        {
            var resource = new T();
            using (resource)
            {
                N = n;
            }
        }
    }

    public static partial class Kernels
    {
        [GpuDeviceFunction]
        public static int ViaTypeParameterDispose(int x) => x + 1;

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UsingLocalKernel(int[] d)
        {
            d[0] = new UsingLocal<Resource>(d[1]).N;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UsingDeclarationKernel(int[] d)
        {
            d[0] = new UsingDeclaration<Resource>(d[1]).N;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UsingResourceKernel(int[] d)
        {
            d[0] = new UsingResource<Resource>(d[1]).N;
        }
    }
}
