using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The first part of a struct split across two files; the other part (PrimaryConstructorPartialPart.cs) reads the
// captured parameter a and declares Y
// [WHY] C# leaves the field order of a struct split across files unspecified (CS0282); Roslyn emits the fields in the
// order of GetMembers(), which the device follows, and the test compares the device offsets with the host ones.
#pragma warning disable CS0282
public partial struct PcPartial(byte a)
{
    public long X = a + 1;
}
#pragma warning restore CS0282

[SkipSnapshotConsistency("PcPartial is split across two files, and the snapshot compiles one file")]
public static partial class PrimaryConstructorPartialKernels
{
    [GpuKernel]
    public static void CreatePartialKernel(long[] output)
    {
        var s = new PcPartial(3);
        output[0] = s.X;
        output[1] = s.Y;
        output[2] = s.Read;
    }

    [GpuKernel]
    public static void RoundtripPartialKernel(ref PcPartial data, long[] layout)
    {
        unsafe
        {
            PcPartial s = default;
            PcPartial* p = &s;
            layout[0] = (long)((byte*)&s.X - (byte*)p);
            layout[1] = (long)((byte*)&s.Y - (byte*)p);
            layout[2] = (long)((byte*)(p + 1) - (byte*)p);
        }

        layout[3] = data.Read;
        data.X = data.X + 1000;
        data.Y = 7;
    }
}
