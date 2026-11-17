using System.Runtime.CompilerServices;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class StructLayoutParityTests
{
    // CPU passes the struct in its managed layout until L4
    [Fact]
    public void InlineArrayOfArrays_ByRef_CopiesEachElementArray()
    {
        var h = new ArrayFieldPairHolder { Tag = 3 };
        h.Arrays[0] = [10, 20];
        h.Arrays[1] = [30, 40, 50];
        var o = new int[3];

        StructLayoutParityKernels.LaunchInlineArrayOfArraysKernel(ref h, o);

        Assert.Equal([3, 20, 3], o);
        Assert.Equal(77, h.Arrays[1][0]);
        Assert.Equal([10, 20], h.Arrays[0]);
    }

    partial void AssertBoolCharByValue()
    {
        var boolCharInput = new BoolAndCharSequential { Flag1 = false, Flag2 = true, Ch = '\u3042', Value = 10 };
        var boolCharOut = new BoolAndCharSequential[1];
        StructLayoutParityKernels.LaunchRoundtripBoolAndCharByValueKernel(boolCharInput, boolCharOut);
        Assert.True(boolCharOut[0].Flag1);
        Assert.False(boolCharOut[0].Flag2);
        Assert.Equal((char)('\u3042' + 1), boolCharOut[0].Ch);
        Assert.Equal(52, boolCharOut[0].Value);
    }

    partial void AssertSystemNumericsVectorsLayout(ref SystemNumericsVectorsStruct s, int[] layout)
    {
        Assert.True(layout[0] > Unsafe.SizeOf<SystemNumericsVectorsStruct>());
    }

    partial void AssertCoreVectorsFloatIntUIntLayout(ref CoreVectorsFloatIntUIntStruct s, int[] layout)
    {
        Assert.True(layout[0] > Unsafe.SizeOf<CoreVectorsFloatIntUIntStruct>());
    }

    partial void AssertCoreVectors64BitLayout(ref CoreVectors64BitStruct s, int[] layout)
    {
        Assert.True(layout[0] > Unsafe.SizeOf<CoreVectors64BitStruct>());
    }
}
