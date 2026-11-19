using System.Numerics;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace EnumFields;

public enum Mode { A, B }

public enum ByteMode : byte { X = 0, Y = 1 }

public enum LongMode : long { Low = 0, High = 1 }

// Three underlying types, an enum behind an auto-property and an enum of a nested struct, each next to an array field
// so that the struct is marshaled member by member
public struct Job
{
    public int[] Data;
    public Mode M;
    public ByteMode BM;
    public LongMode LM;
    public Mode P { get; set; }
    public Inner Nested;

    public struct Inner
    {
        public Mode NM;
    }
}

// One member of each of the other kinds of shadow field
public unsafe struct Shapes
{
    public int Count;
    public Vector3 Direction;
    public fixed int Buffer[4];
}

[InlineArray(3)]
public struct Triple
{
    private int _element;
}

// A private enum cannot be named by the launcher: the launch is refused (GSH0108) and no marshal method is written
public struct HiddenJob
{
    private enum Level { Low, High }

    public int[] Data;
    private Level _level;
}

public static partial class JobKernels
{
    [GpuKernel]
    public static void Run(Job job)
    {
        int tid = ThreadIndex.X;
        job.Data[tid] = job.M == Mode.B && job.BM == ByteMode.Y && job.LM == LongMode.High ? 1 : 0;
    }

    [GpuKernel]
    public static void RunHidden(HiddenJob job)
    {
        job.Data[ThreadIndex.X] = 1;
    }
}

// What the shadow struct and the host mirror declare for each kind of member: a private enum of each underlying type,
// an enum field the launcher can name, a private field of a public enum type and a private field of another type
public struct StorageProbe
{
    private enum ByteLevel : byte { A }

    private enum LongLevel : long { A }

    private enum ULongLevel : ulong { A }

    public int[] Data;
    private ByteLevel _byteLevel;
    private LongLevel _longLevel;
    private ULongLevel _ulongLevel;
    public Mode PublicMode;
    private Mode _privateMode;
    private int _count;
}
