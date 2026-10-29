// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A struct declared in one switch section and assigned in another (<see cref="SwitchSectionLocalKernels"/>).</summary>
public struct SwitchPair
{
    public int A;
    public int B;

    public SwitchPair(int a, int b)
    {
        A = a;
        B = b;
    }
}

/// <summary>
/// A struct with an array field: its C++ form has a default constructor, so a jump may not cross its declaration.
/// </summary>
public struct SwitchHolder
{
    public int[] Values;
    public int Count;
}

/// <summary>
/// Kernels whose switch sections share a local. In C# the switch block is one scope, so a local declared in one section
/// can be assigned and read in a later section; the jump to that section skips its initializer.
/// </summary>
public static partial class SwitchSectionLocalKernels
{
    // The form of #795: x is declared with an initializer in case 0 and assigned in case 1
    [GpuKernel]
    public static void InitializedInFirstSectionKernel(int[] output, int flag)
    {
        switch (flag)
        {
            case 0:
                int x = 1;
                output[0] = x;
                break;
            case 1:
                x = 5;
                output[0] = x + 1;
                break;
        }
    }

    // A struct with a constructor, a var local, a declaration without an initializer and several declarators in one
    // statement, of which only some are used in other sections; the default section uses them too
    [GpuKernel]
    public static void SharedStructAndVarKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        switch (data[tid] % 3)
        {
            case 0:
                SwitchPair pair = new SwitchPair(data[tid], 2);
                var scaled = pair.A * 10;
                int unused = 7, shared = scaled + unused;
                int later;
                later = shared + pair.B;
                data[tid] = later;
                break;
            case 1:
                pair = new SwitchPair(1, data[tid]);
                scaled = pair.B * 100;
                shared = scaled + 3;
                data[tid] = shared;
                break;
            default:
                later = data[tid] + 1000;
                data[tid] = later;
                break;
        }
    }

    // A section goes on to the next one through goto case, which assigns the local of the first section again, and a
    // nested switch in a section has a shared local of its own
    [GpuKernel]
    public static void GotoCaseAndNestedSwitchKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        switch (data[tid] % 2)
        {
            case 0:
                int carried = data[tid] * 2;
                data[tid] = carried;
                goto case 1;
            case 1:
                carried = data[tid];
                switch (tid % 2)
                {
                    case 0:
                        int inner = 3;
                        carried += inner;
                        break;
                    default:
                        inner = 4;
                        carried += inner * 10;
                        break;
                }

                data[tid] = carried;
                break;
        }
    }

    // A const local of a section named in a later section, which the jump enters without passing the declaration
    [GpuKernel]
    public static void SectionConstantKernel(int[] output, int flag)
    {
        switch (flag)
        {
            case 0:
                const int Offset = 40;
                output[0] = Offset + 1;
                break;
            default:
                output[0] = Offset + 2;
                break;
        }
    }

    // A byte const keeps its type in the other section: x++ wraps to 0 as in C#
    [GpuKernel]
    public static void ByteConstantKernel(int[] output, int flag)
    {
        switch (flag)
        {
            case 0:
                const byte Max = 255;
                var x = Max;
                x++;
                output[0] = x;
                break;
            default:
                var y = Max;
                y++;
                output[0] = y + Max;
                break;
        }
    }

    [GpuDeviceFunction]
    public static int ReadIn(in int value) => value;

    // A section const passed as an in argument, in its own section and in another
    [GpuKernel]
    public static void ConstantInArgumentKernel(int[] output, int flag)
    {
        switch (flag)
        {
            case 0:
                const int K = 40;
                output[0] = ReadIn(K);
                break;
            default:
                output[0] = ReadIn(K) + 1;
                break;
        }
    }

    // A struct with an array field and an array alias shared by the sections
    [GpuKernel]
    public static void ArrayFieldStructKernel(int[] data, int[] other, int flag)
    {
        switch (flag)
        {
            case 0:
                SwitchHolder holder;
                holder.Values = data;
                holder.Count = 2;
                int[] alias = other;
                alias[0] = holder.Values[0] + holder.Count;
                break;
            default:
                holder.Values = other;
                holder.Count = 5;
                alias = data;
                alias[0] = holder.Values[0] + holder.Count;
                break;
        }
    }

    // A goto past a struct with an array field (the split of #768)
    [GpuKernel]
    public static void GotoPastArrayFieldStructKernel(int[] data, int flag)
    {
        SwitchHolder source;
        source.Values = data;
        source.Count = 3;
        if (flag == 0)
        {
            goto skip;
        }

        SwitchHolder holder = source;
        data[0] = holder.Values[0] + holder.Count;
    skip:
        data[1] = 7;
    }

    // A warp shuffle before the declaration: the CPU lowering adds a temporary to the section
    [GpuKernel]
    public static void ShuffleBeforeDeclarationKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        switch (flag)
        {
            case 0:
                output[tid] = Warp.ShuffleXorSync(0xFFFFFFFFu, tid, 1);
                int x = 1;
                output[tid] += x;
                break;
            default:
                x = 2;
                output[tid] = x + tid;
                break;
        }
    }
}
