using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Layout;

// Generic structs whose layout is read through a constructed type (#736). From metadata, neither a constructed type nor
// its fields have a metadata token of their own; the reader maps them to the TypeDef and FieldDef rows of the
// definition. Not compiled into a test assembly: the CLR refuses to load a generic type with explicit layout.

// PackedGeneric<double>: 9 bytes on the host (V at 1)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackedGeneric<T> where T : unmanaged
{
    public byte B;
    public T V;
}

// ExplicitGeneric<double>: V at 8
[StructLayout(LayoutKind.Explicit)]
public struct ExplicitGeneric<T> where T : unmanaged
{
    [FieldOffset(0)]
    public byte B;

    [FieldOffset(8)]
    public T V;
}

// GenericOuter<double>.ExplicitInner: a struct nested in a constructed generic type, I at 4 and V at 16
public struct GenericOuter<T> where T : unmanaged
{
    public T Value;

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct ExplicitInner
    {
        [FieldOffset(4)]
        public int I;

        [FieldOffset(16)]
        public T V;
    }
}
