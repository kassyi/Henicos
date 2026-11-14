// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels whose switch sections share an expression variable or a local function. In C# the switch block is one
/// scope for them too, so a section can use what another section declares.
/// </summary>
public static partial class SwitchSectionSharedNamesKernels
{
    [GpuDeviceFunction]
    public static bool TryDouble(int x, out int result)
    {
        result = x * 2;
        return true;
    }

    // An out variable that the first section declares and the second assigns
    [GpuKernel]
    public static void OutVarAcrossSectionsKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        switch (input[t])
        {
            case 0:
                TryDouble(5, out var v);
                output[t] = v;
                break;
            case 1:
                v = 7;
                output[t] = v;
                break;
        }
    }

    // The variables of a deconstruction declaration
    [GpuKernel]
    public static void DeconstructionAcrossSectionsKernel(int[] output, int mode)
    {
        switch (mode)
        {
            case 0:
                var (a, b) = (1, 2);
                output[0] = a + b;
                break;
            default:
                a = 3;
                b = 4;
                output[0] = a * b;
                break;
        }
    }

    // A local function that the first section declares and the second calls
    [GpuKernel]
    public static void LocalFunctionAcrossSectionsKernel(int[] output, int mode)
    {
        switch (mode)
        {
            case 0:
                int Next(int v) => v + 1;
                output[0] = Next(1);
                break;
            default:
                output[0] = Next(2);
                break;
        }
    }

    // The local function captures a local of its section, which the other section assigns before the call
    [GpuKernel]
    public static void CapturingLocalFunctionAcrossSectionsKernel(int[] output, int mode)
    {
        switch (mode)
        {
            case 0:
                int bonus = 10;
                int Add(int v) => v + bonus;
                output[0] = Add(1);
                break;
            default:
                bonus = 100;
                output[0] = Add(2);
                break;
        }
    }

    // The shared local function calls a local function of its own section
    [GpuKernel]
    public static void ChainedLocalFunctionAcrossSectionsKernel(int[] output, int mode)
    {
        switch (mode)
        {
            case 0:
                int Inner(int v) => v * 3;
                int Outer(int v) => Inner(v) + 1;
                output[0] = Outer(2);
                break;
            default:
                output[0] = Outer(5);
                break;
        }
    }
}
