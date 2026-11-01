using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Structs for TransferPlannerTests whose plans are decided below the struct itself: a Nullable<T>, array or inline
// array member that cannot cross the boundary, a Nullable<T> of an auto-layout struct, and the member kinds. Roslyn
// only, not compiled into the test assembly.
namespace TransferPlanCasesTest
{
    public struct Priced { public int Id; public decimal Price; }

    [StructLayout(LayoutKind.Explicit)]
    public struct Misaligned { [FieldOffset(0)] public byte A; [FieldOffset(1)] public int B; }

    [StructLayout(LayoutKind.Sequential, Size = 6)]
    public struct OddHead { public int A; }

    public struct ShiftedFields { public OddHead Head; public byte Tail; }

    [StructLayout(LayoutKind.Explicit)]
    public struct BrokenUnion { [FieldOffset(0)] public ShiftedFields Converted; [FieldOffset(0)] public long RawBits; }

    public struct Bag { public int N; public int[] Data; }

    [StructLayout(LayoutKind.Auto)]
    public struct AutoPair { public byte A; public long B; }

    // The layout model accepts these structs; one member's own plan is unsupported
    public struct NullablePricedHolder { public int Tag; public Priced? Item; }

    public struct NullableMisalignedHolder { public Misaligned? Item; }

    public struct NullableBagHolder { public Bag? Item; }

    public struct DecimalArrayHolder { public int Tag; public decimal[] Items; }

    public struct PricedArrayHolder { public Priced[] Items; }

    public struct NestedHolder { public int Tag; public NullablePricedHolder Holder; }

    [InlineArray(2)]
    public struct PricedOptions { private Priced? _element0; }

    public struct PricedOptionsHolder { public PricedOptions Options; }

    // A Nullable<T> of an auto-layout struct: the host layout is the runtime's, the value converts as AutoPair does
    public struct AutoPairHolder { public int Tag; public AutoPair? Item; }

    [InlineArray(1)]
    public struct One { private int _element0; }

    public unsafe struct MemberKinds(int captured)
    {
        public int Field;
        public int Property { get; set; }
        public fixed int Buffer[2];
        public One Single;
        public int Read => captured;
    }
}
