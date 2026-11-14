using System;
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

    // A span over an array that the first section declares and the second assigns: the view is declared first
    [GpuKernel]
    public static void SpanAcrossSectionsKernel(int[] a, int[] b, int mode)
    {
        switch (mode)
        {
            case 0:
                Span<int> view = a;
                view[0] = 1;
                break;
            default:
                view = b;
                view[0] = 2;
                break;
        }
    }

    // goto case from the section that declares the out variable to the one that assigns it
    [GpuKernel]
    public static void GotoCaseSharedOutVarKernel(int[] output)
    {
        switch (output[0])
        {
            case 0:
                TryDouble(5, out var v);
                output[1] = v;
                goto case 1;
            case 1:
                v = 7;
                output[2] = v;
                break;
        }
    }

    // The switch is in a loop: the variable is declared again on every pass
    [GpuKernel]
    public static void SharedInLoopKernel(int[] output)
    {
        for (int i = 0; i < 2; i++)
        {
            switch (i)
            {
                case 0:
                    TryDouble(i + 3, out var v);
                    output[0] = v;
                    break;
                default:
                    v = 5;
                    output[1] = v;
                    break;
            }
        }
    }

    // A switch in a section of another switch: each one declares its own shared variable
    [GpuKernel]
    public static void NestedSwitchSharedKernel(int[] output, int outer, int inner)
    {
        switch (outer)
        {
            case 0:
                TryDouble(1, out var a);
                switch (inner)
                {
                    case 0:
                        TryDouble(2, out var b);
                        output[0] = a + b;
                        break;
                    default:
                        b = 10;
                        output[0] = a + b;
                        break;
                }

                break;
            default:
                a = 20;
                output[0] = a;
                break;
        }
    }

    // The shared function is used above its own declaration, in its own section
    [GpuKernel]
    public static void SharedFunctionUsedAboveDeclarationKernel(int[] output, int mode)
    {
        switch (mode)
        {
            case 0:
                output[0] = Twice(3);
                int Twice(int v) => v * 2;
                break;
            default:
                output[0] = Twice(4);
                break;
        }
    }

    // A function of the first section calls the shared function that the second section declares
    [GpuKernel]
    public static void FunctionCallsLaterSectionFunctionKernel(int[] output, int mode)
    {
        switch (mode)
        {
            case 0:
                int Add(int v) => Times(v) + 1;
                output[0] = Add(1);
                break;
            default:
                int Times(int v) => v * 10;
                output[0] = Times(2);
                break;
        }
    }

    // A recursive function that two sections use
    [GpuKernel]
    public static void SharedRecursiveFunctionKernel(int[] output, int mode)
    {
        switch (mode)
        {
            case 0:
                int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
                output[0] = Factorial(3);
                break;
            default:
                output[0] = Factorial(4);
                break;
        }
    }
}
