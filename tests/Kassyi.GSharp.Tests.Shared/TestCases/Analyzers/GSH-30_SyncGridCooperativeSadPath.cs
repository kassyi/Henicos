using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class GSH_30_SyncGridCooperativeSadPath
{
    [GpuKernel] // Sad Path: Default (Cooperative = false implicit)
    public static void NonCooperativeKernelWithSyncGrid()
    {
        int sum = 0;

        // Error GSH0030: Block lambda requires Cooperative = true
        Gpu.SyncGrid(() =>
        {
            sum += 1;
            sum += 2;
        });

        // Error GSH0030: Parameterless SyncGrid requires Cooperative = true
        Gpu.SyncGrid();
    }

    [GpuKernel(Cooperative = false)] // Boundary: Explicit Cooperative = false
    public static void ExplicitFalseCooperativeKernel()
    {
        int sum = 0;

        // Error GSH0030: Block lambda with explicit Cooperative = false
        Gpu.SyncGrid(() =>
        {
            sum += 1;
        });

        // Error GSH0030: Parameterless SyncGrid with explicit Cooperative = false
        Gpu.SyncGrid();
    }

    [GpuKernel] // Boundary: Empty block lambda & Expression-bodied lambda
    public static void EmptyAndExpressionLambdaBoundary()
    {
        int sum = 0;

        // Error GSH0030: Empty lambda block still requires Cooperative = true
        Gpu.SyncGrid(() => { });

        // Error GSH0030: Expression-bodied lambda requires Cooperative = true
        Gpu.SyncGrid(() => sum += 1);
    }

    [GpuKernel] // Boundary: Nested SyncGrid pattern in non-cooperative kernel
    public static void NestedSyncGridInNonCooperativeKernel()
    {
        int sum = 0;

        // Error GSH0030: Outer SyncGrid requires Cooperative = true
        // Error GSH0030: Inner SyncGrid also requires Cooperative = true
        Gpu.SyncGrid(() =>
        {
            Gpu.SyncGrid(() =>
            {
                sum += 1;
            });
        });
    }

    [GpuKernel(Cooperative = true)] // Happy Path Boundary: Cooperative = true should produce 0 errors
    public static void CooperativeKernelValid()
    {
        int sum = 0;
        Gpu.SyncGrid(() =>
        {
            sum += 1;
        });
        Gpu.SyncGrid();
        Gpu.SyncGrid(() => { });

        // Nested SyncGrid produces 0 errors in cooperative kernel
        Gpu.SyncGrid(() =>
        {
            Gpu.SyncGrid(() =>
            {
                sum += 10;
            });
        });
    }

    [GpuDeviceFunction] // Boundary: [GpuDeviceFunction] should produce 0 errors (delegated to caller kernel)
    public static void DeviceFunctionValid()
    {
        int sum = 0;
        Gpu.SyncGrid();
        Gpu.SyncGrid(() =>
        {
            sum += 1;
        });
    }
}
