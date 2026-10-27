public struct Outer<TOuter>
{
    public struct Inner
    {
        public int Value;
    }
}

public class TestClass
{
    public static void K<T>()
    {
        Outer<T>.Inner x = default;
    }
}
