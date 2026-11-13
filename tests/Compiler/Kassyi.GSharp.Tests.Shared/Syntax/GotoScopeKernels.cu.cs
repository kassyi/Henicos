// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels whose <c>goto</c> jumps past an initialized declaration that is not in the statement list of the goto: in a
/// nested block, in another switch section, or under a label. C# allows the jump; C++ forbids a jump into the scope of
/// an initialized declaration ([stmt.dcl]), and the transpiler writes a nested block without braces, so the scopes of
/// the two languages differ.
/// </summary>
public static partial class GotoScopeKernels
{
    // The goto jumps over a whole nested block
    [GpuKernel]
    public static void GotoOverNestedBlockKernel(int[] data, int flag)
    {
        if (flag == 0)
        {
            goto skip;
        }

        {
            int x = flag + 1;
            data[0] = x;
        }

    skip:
        data[1] = 7;
    }

    // The goto jumps over two nested blocks
    [GpuKernel]
    public static void GotoOverDoublyNestedBlockKernel(int[] data, int flag)
    {
        if (flag == 0)
        {
            goto skip;
        }

        {
            {
                int x = flag + 4;
                data[0] = x;
            }
        }

    skip:
        data[1] = 7;
    }

    // The goto jumps over a labeled nested block
    [GpuKernel]
    public static void GotoOverLabeledBlockKernel(int[] data, int flag)
    {
        if (flag == 0)
        {
            goto skip;
        }

#pragma warning disable CS0164 // The label under test has no goto
    block:
        {
            int x = flag + 5;
            data[0] = x;
        }
#pragma warning restore CS0164

    skip:
        data[1] = 7;
    }

    // The goto leaves the nested block that declares the local after it
    [GpuKernel]
    public static void GotoOutOfNestedBlockKernel(int[] data, int flag)
    {
        {
            if (flag == 0)
            {
                goto skip;
            }

            int z = flag * 3;
            data[0] = z;
        }

    skip:
        data[1] = 7;
    }

    // A forward goto from one switch section to a label in another
    [GpuKernel]
    public static void GotoForwardAcrossSectionsKernel(int[] data, int flag)
    {
        switch (flag)
        {
            case 0:
                goto skip;
            case 1:
                int x = flag + 5;
                data[0] = x;
            skip:
                data[1] = 7;
                break;
        }
    }

    // A backward goto from one switch section to a label in an earlier one
    [GpuKernel]
    public static void GotoBackwardAcrossSectionsKernel(int[] data, int flag)
    {
        switch (flag)
        {
            case 1:
                int x = flag + 2;
                data[0] = x;
            skip:
                data[1] = 7;
                break;
            case 0:
                goto skip;
        }
    }

    // The jumped-over declaration is the statement of a label
    [GpuKernel]
    public static void GotoPastLabeledDeclarationKernel(int[] data, int flag)
    {
        if (flag == 0)
        {
            goto done;
        }

        goto declaration;
    declaration:
        int a = flag + 2;
        data[0] = a;
    done:
        a = 7;
        data[1] = a;
    }

    // A struct with an array field (a C++ default constructor) in a nested block that the goto jumps over
    [GpuKernel]
    public static void GotoOverNestedStructKernel(int[] data, int flag)
    {
        if (flag == 0)
        {
            goto skip;
        }

        {
            SwitchHolder holder = new SwitchHolder { Values = data, Count = flag + 1 };
            data[0] = holder.Values[1] + holder.Count;
        }

    skip:
        data[1] = 7;
    }

    // A struct with an array field in a switch section that a goto from another section jumps into
    [GpuKernel]
    public static void GotoAcrossSectionsPastStructKernel(int[] data, int flag)
    {
        switch (flag)
        {
            case 0:
                goto skip;
            case 1:
                SwitchHolder holder = new SwitchHolder { Values = data, Count = flag + 1 };
                data[0] = holder.Values[1] + holder.Count;
            skip:
                data[1] = 7;
                break;
        }
    }

    // A labeled struct with an array field
    [GpuKernel]
    public static void GotoPastLabeledStructKernel(int[] data, int flag)
    {
        if (flag == 0)
        {
            goto done;
        }

        goto declaration;
    declaration:
        SwitchHolder holder = new SwitchHolder { Values = data, Count = flag + 2 };
        data[0] = holder.Values[1] + holder.Count;
    done:
        data[1] = 7;
    }
}
