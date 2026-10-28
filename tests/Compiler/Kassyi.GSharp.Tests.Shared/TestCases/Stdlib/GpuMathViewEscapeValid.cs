// Correct uses of the GpuMath views; GpuMathViewEscapeTests checks that this file compiles without diagnostics, which
// shows that the errors of GpuMathViewEscape.cs come from the escapes and not from a broken reference.
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

namespace GpuMathViewEscapeValidCases;

public static class Uses
{
    // A local view passed to an operation together with an owned value
    public static void Local(GpuBigUIntArray array, GpuBigUInt owned, GpuBigUInt result)
    {
        var view = array.GetElement(0);
        GpuBigIntMath.Add(view, owned, result);
        _ = owned.CompareTo(array.GetElement(1));
    }

    // ToOwned keeps the value after the call
    public static GpuBigUInt Kept(GpuBigUIntArray array) => array.GetElement(0).ToOwned();

    // A view passed to a scoped parameter, and a ref struct field of a ref struct
    public static int Scoped(scoped GpuBigUIntView view) => view.NumWords;

    public ref struct Holder
    {
        public GpuBigUIntView View;
    }

    // RS10: with J12 (no ref field) returning a view of a local owner compiles; the generation catches the use
    public static GpuBigUIntView ViewOfLocal(GpuBigUIntArray array)
    {
        var local = array.GetElement(0).ToOwned();
        local.Dispose();
        return local;
    }

    public static void SignedAndFloat(GpuBigIntArray ints, GpuBigFloatArray floats, GpuBigFloat value)
    {
        GpuBigIntView element = ints.GetElement(0);
        _ = element.IsNegative;
        floats.SetElement(0, floats.GetElement(1));
        floats.SetElement(1, value);
        GpuBigFloatArrayView slice = floats.Slice(0, 1);
        _ = slice.Length;
    }
}
