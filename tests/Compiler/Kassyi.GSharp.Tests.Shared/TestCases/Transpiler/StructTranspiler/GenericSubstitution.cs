namespace Kassyi.GSharp.Tests.TestCases.StructTranspiler;

public struct Pair<TA, TB>
{
    public TA A;
    public TB B;
}

public struct Inner<TI>
{
    public TI Value;
}

public unsafe struct Shapes<T>
    where T : unmanaged
{
    public T Plain;
    public T* Pointer;
    public T[] Array;
    public Inner<T> Nested;
    public Pair<int, T> SecondArgument;
    public Pair<int, int> Closed;
    public int Scalar;
    public int[] PlainArray;
    public int* PlainPointer;
}

public struct NotGeneric
{
    public int Value;
}

public static class Uses
{
    public static void Generic<TMethod>(Shapes<float> closed, Pair<int, float> pair, NotGeneric plain, TMethod methodTypeParameter)
    {
    }
}
