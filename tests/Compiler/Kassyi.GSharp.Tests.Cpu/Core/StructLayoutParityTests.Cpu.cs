using System.Runtime.CompilerServices;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class StructLayoutParityTests
{
    partial void AssertSystemNumericsVectorsLayout(ref SystemNumericsVectorsStruct s, int[] layout)
    {
        Assert.Equal(Unsafe.SizeOf<SystemNumericsVectorsStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.Tag1), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.V2), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.Tag2), layout[3]);
        Assert.Equal(HostOffset(ref s, ref s.V3), layout[4]);
        Assert.Equal(HostOffset(ref s, ref s.Tag3), layout[5]);
        Assert.Equal(HostOffset(ref s, ref s.V4), layout[6]);
    }

    partial void AssertCoreVectorsFloatIntUIntLayout(ref CoreVectorsFloatIntUIntStruct s, int[] layout)
    {
        Assert.Equal(Unsafe.SizeOf<CoreVectorsFloatIntUIntStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.Tag1), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.F2), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.F3), layout[3]);
        Assert.Equal(HostOffset(ref s, ref s.F4), layout[4]);
        Assert.Equal(HostOffset(ref s, ref s.Tag2), layout[5]);
        Assert.Equal(HostOffset(ref s, ref s.I2), layout[6]);
        Assert.Equal(HostOffset(ref s, ref s.I3), layout[7]);
        Assert.Equal(HostOffset(ref s, ref s.I4), layout[8]);
        Assert.Equal(HostOffset(ref s, ref s.Tag3), layout[9]);
        Assert.Equal(HostOffset(ref s, ref s.U2), layout[10]);
        Assert.Equal(HostOffset(ref s, ref s.U3), layout[11]);
        Assert.Equal(HostOffset(ref s, ref s.U4), layout[12]);
    }

    partial void AssertCoreVectors64BitLayout(ref CoreVectors64BitStruct s, int[] layout)
    {
        Assert.Equal(Unsafe.SizeOf<CoreVectors64BitStruct>(), layout[0]);
        Assert.Equal(HostOffset(ref s, ref s.Tag1), layout[1]);
        Assert.Equal(HostOffset(ref s, ref s.D2), layout[2]);
        Assert.Equal(HostOffset(ref s, ref s.D3), layout[3]);
        Assert.Equal(HostOffset(ref s, ref s.D4), layout[4]);
        Assert.Equal(HostOffset(ref s, ref s.Tag2), layout[5]);
        Assert.Equal(HostOffset(ref s, ref s.L2), layout[6]);
        Assert.Equal(HostOffset(ref s, ref s.L3), layout[7]);
        Assert.Equal(HostOffset(ref s, ref s.L4), layout[8]);
        Assert.Equal(HostOffset(ref s, ref s.Tag3), layout[9]);
        Assert.Equal(HostOffset(ref s, ref s.UL2), layout[10]);
        Assert.Equal(HostOffset(ref s, ref s.UL3), layout[11]);
        Assert.Equal(HostOffset(ref s, ref s.UL4), layout[12]);
    }
}
