using System.Runtime.CompilerServices;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class StructLayoutParityTests
{
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
