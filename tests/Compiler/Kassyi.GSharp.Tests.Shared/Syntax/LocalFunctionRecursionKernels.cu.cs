// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Recursive local functions. C# allows a local function to call itself and the local functions of its group, with no
/// barrier in them; a C++ lambda cannot call itself through the <c>auto</c> variable that it initializes. The recursion
/// spills on purpose (GS0001).
/// </summary>
#pragma warning disable GS0001
public static partial class LocalFunctionRecursionKernels
{
    // A local function that calls itself and captures a local and a parameter
    [GpuKernel]
    public static void SelfRecursionKernel(int[] data, int bonus)
    {
        int tid = ThreadIndex.X;
        int step = 2;
        data[tid] = Sum(data[tid]);

        int Sum(int n) => n <= 0 ? bonus : n * step + Sum(n - 1);
    }

    // Two local functions that call each other, and a counter that both change
    [GpuKernel]
    public static void MutualRecursionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int calls = 0;
        int result = IsEven(data[tid]);
        data[tid] = result * 100 + calls;

        int IsEven(int n)
        {
            calls++;
            return n == 0 ? 1 : IsOdd(n - 1);
        }

        int IsOdd(int n)
        {
            calls++;
            return n == 0 ? 0 : IsEven(n - 1);
        }
    }

    // A recursive local function that returns nothing and writes through a captured array
    [GpuKernel]
    public static void VoidRecursionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        Fill(data[tid], 0);

        void Fill(int n, int depth)
        {
            if (n <= 0)
            {
                return;
            }

            data[tid] += depth;
            Fill(n - 1, depth + 1);
        }
    }

    // A recursive local function that is used above its declaration and calls a later non-recursive one
    [GpuKernel]
    public static void RecursionBeforeDeclarationKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = Count(data[tid]);

        int Count(int n) => n <= 0 ? 0 : Bump(Count(n - 1));
        int Bump(int x) => x + 3;
    }

    // A recursive local function converted to a delegate above its declaration
    [GpuKernel]
    public static void DelegateOfRecursiveKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        Func<int, int> pick = Factorial;
        data[tid] = pick(data[tid]);

        int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
    }
}
#pragma warning restore GS0001
