using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Core.Qualification;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Locals of a <c>Gpu.GridStrideLoop</c>, <c>Gpu.ForUnroll</c> or <c>Gpu.SingleThread</c> lambda that shadow a local
/// or parameter of the method, which C# 8 and later allow. The managed fallback copies the lambda body into the method.
/// </summary>
public static partial class ManagedShadowedLocalKernels
{
    // result[i] = 2i from the lambda's v; thread 0 then adds the method's v (1) to result[0]
    [GpuKernel]
    public static void GridStrideLoopKernel(int[] result, int n)
    {
        int v = 1;
        Gpu.GridStrideLoop(n, i =>
        {
            int v = i * 2;
            result[i] = v;
        });
        if (ThreadIndex.X == 0)
        {
            result[0] += v;
        }
    }

    // result[0] = 2 from the lambda's v, result[1] = 1 from the method's v
    [GpuKernel]
    public static void SingleThreadKernel(int[] result)
    {
        int v = 1;
        Gpu.SingleThread(() =>
        {
            int v = 2;
            result[0] = v;
        });
        if (ThreadIndex.X == 0)
        {
            result[1] = v;
        }
    }

    // result[k] = k from the lambda's v (k < 4), result[4] = 1 from the method's v
    [GpuKernel]
    public static void ForUnrollKernel(int[] result)
    {
        int v = 1;
        Gpu.ForUnroll(0, 4, k =>
        {
            int v = k;
            result[k] = v;
        });
        result[4] = v;
    }

    // A local declared after the call, a lambda local named like a parameter, an out variable, and a ForUnroll lambda
    // inside a GridStrideLoop lambda that shadows the outer lambda's local:
    // result[i] = i (n) + 10 * (0 + 1) (inner v) + 100 * i (outer v) + 1000 (out w); thread 0 then adds 3 (later)
    [GpuKernel]
    public static void NestedAndLaterKernel(int[] result, int n)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            int v = i;
            int sum = 0;
            Gpu.ForUnroll(0, 2, k =>
            {
                int v = k;
                sum += v * 10;
            });
            int n = i;
            result[i] = n + sum + v * 100 + (TryThousand(out int w) ? w : 0);
        });
        int later = 3;
        int w = 0;
        if (ThreadIndex.X == 0)
        {
            result[0] += later + w;
        }
    }

    // A local function of the lambda named like a local of the kernel, and a lambda local named like a local function
    // of the kernel: result[i] = 3i + 10; thread 0 then adds 1 (v) + 100 (Hundred())
    [GpuKernel]
    public static void LocalFunctionKernel(int[] result, int n)
    {
        // Declared before its use: the native output does not hoist a local function (C3861 otherwise)
        static int Hundred() => 100;

        int v = 1;
        Gpu.GridStrideLoop(n, i =>
        {
            int v() => i * 3;
            int Hundred = 10;
            result[i] = v() + Hundred;
        });
        if (ThreadIndex.X == 0)
        {
            result[0] += v + Hundred();
        }
    }

    [GpuDeviceFunction]
    public static bool TryThousand(out int value)
    {
        value = 1000;
        return true;
    }

    // The same shadowing in a device function with a managed version (it holds an operator macro), whose body is the
    // user's own: result[i] = 2i + 3 (the macro sum of the method's v and 2) for i >= 1, result[0] = 7 + 3
    [GpuDeviceFunction]
    public static void ShadowInManagedVersion(int[] result, int n)
    {
        QualifyWide once;
        once.V = 1;
        QualifyWide twice;
        twice.V = 2;
        QualifyWide sum = default;
        sum = once + twice;
        int v = sum.V;
        Gpu.GridStrideLoop(n, i =>
        {
            int v = i * 2;
            result[i] = v;
        });
        Gpu.SingleThread(() =>
        {
            int v = 7;
            result[0] = v;
        });
        for (int i = 0; i < n; i++)
        {
            if (ThreadIndex.X == i)
            {
                result[i] += v;
            }
        }
    }

    [GpuKernel]
    public static void ManagedVersionKernel(int[] result, int n) => ShadowInManagedVersion(result, n);
}
