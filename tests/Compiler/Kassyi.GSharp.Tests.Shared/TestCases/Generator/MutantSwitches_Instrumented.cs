using System.Collections.Generic;
using Kassyi.GSharp.Core;

// The device code of MutantSwitches_Original.cs as Stryker.NET 5.0.0 mutates it, with the helper class Stryker adds to
// the compilation: MutantSwitchGeneratorTests compares the managed code the generator emits for the two files
namespace StrykerAbCdEfGhIjKlMnO
{
    public static class MutantControl
    {
        public static bool IsActive(int id) => false;
    }
}

namespace MutantSwitchGeneratorCase
{
    public struct Counter
    {
        public int Calls;
    }

    public struct Value
    {
        public int V;

        public Value(int v) => V = v;

        [GpuOperatorMacro(typeof(Kernels), nameof(Kernels.Add))]
        public static Value operator +(Value left, Value right) => throw new GpuOnlyException();
    }

    public static partial class Kernels
    {
        [GpuDeviceFunction]
        public static void Add(ref Counter counter, ref Value result, Value left, Value right)
        {
            counter.Calls++;
            result.V = left.V + right.V;
        }

        // Expands the operator macro, so the managed path runs a managed version of it
        [GpuDeviceFunction]
        public static int Twice(ref Counter counter, int a)
        {
            Value x = new Value(a);
            Value z = new Value(0);
            z = x + x;
            return (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(0) ? z.V / 2 : z.V * 2);
        }

        // Inlined where the kernel enumerates it
        [GpuDeviceFunction]
        public static IEnumerable<int> Range(int count)
        {
            for (int i = 0; (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(1) ? i <= count : i < count); i++)
            {
                yield return i;
            }
        }

        // Reaches a barrier, so the managed path runs a state machine of it
        [GpuDeviceFunction]
        public static int Exchange(int[] shared, int value)
        {
            shared[ThreadIndex.X] = value;
            if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(2))
            {
                ;
            }
            else
            {
                Gpu.SyncThreads();
            }
            return shared[(StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(3) ? (ThreadIndex.X + 1) * BlockDimension.X : (ThreadIndex.X + 1) % BlockDimension.X)];
        }

        [GpuKernel]
        public static void SwitchKernel(int[] input, int[] shared, int[] output)
        {
            int tid = ThreadIndex.X;
            int sum = 0;
            foreach (var x in Range(3))
            {
                if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(4))
                {
                }
                else
                {
                    sum += x;
                }
            }

            Counter counter = default;
            sum += Twice(ref counter, input[tid]);
            sum += Exchange(shared, input[tid]);
            if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(5))
            {
                ;
            }
            else
            {
                Gpu.SyncThreads();
            }
            if ((StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(6) ? sum >= 0 : sum > 0))
            {
                if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(7))
                {
                }
                else
                {
                    output[tid] = sum;
                }
            }
            else
            {
                output[tid] = (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(8) ? sum : -sum);
            }
        }
    }
}
