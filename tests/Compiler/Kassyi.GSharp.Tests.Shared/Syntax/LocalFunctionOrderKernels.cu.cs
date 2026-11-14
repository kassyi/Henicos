// ReSharper disable All
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels that use a local function before the statement that declares it, and recursive local functions. C# allows
/// both; a C++ lambda can be named only after its definition and cannot call itself through an <c>auto</c> variable.
/// </summary>
public static partial class LocalFunctionOrderKernels
{
    // The call is above the declaration
    [GpuKernel]
    public static void CallBeforeDeclarationKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = Next(data[tid]);

        static int Next(int x) => x + 1;
    }

    // The function captures a local that is declared before the call, and the call is above the declaration
    [GpuKernel]
    public static void CapturingCallBeforeDeclarationKernel(int[] data, int bonus)
    {
        int tid = ThreadIndex.X;
        int scale = bonus * 2;
        data[tid] = Scaled(data[tid]);

        int Scaled(int x) => x * scale + bonus;
    }

    // A later local function is used only by an earlier local function, which the statement above calls
    [GpuKernel]
    public static void CallChainBeforeDeclarationKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = Outer(data[tid]);

        int Outer(int x) => Inner(x) * 2;
        int Inner(int x) => x + 10;
    }

    // The call is inside a nested block
    [GpuKernel]
    public static void NestedCallBeforeDeclarationKernel(int[] data, int flag)
    {
        int tid = ThreadIndex.X;
        if (flag != 0)
        {
            data[tid] = Next(data[tid]);
        }

        int Next(int x) => x + 100;
    }

    // The call is above the declaration inside one switch section
    [GpuKernel]
    public static void SwitchSectionCallBeforeDeclarationKernel(int[] data, int mode)
    {
        int tid = ThreadIndex.X;
        switch (mode)
        {
            case 0:
                data[tid] = Next(data[tid]);
                int Next(int x) => x + 1000;
                break;
            default:
                data[tid] = -1;
                break;
        }
    }

    // The call is above the declaration, in the statements that a using declaration encloses
    [GpuKernel]
    public static void UsingBodyCallBeforeDeclarationKernel(int[] log, int[] source, int[] outs)
    {
        using var resource = new FailingResource { Log = log, Source = source, Id = 1, Fail = 0 };
        outs[0] = Next(outs[0]);

        int Next(int x) => x + 500;
    }
}
