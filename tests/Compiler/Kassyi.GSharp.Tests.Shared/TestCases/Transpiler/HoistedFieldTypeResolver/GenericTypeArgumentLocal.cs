public struct Wrapper<TValue>
{
    public TValue Value;
}

public class TestClass
{
    public static void K<T>()
    {
        Wrapper<int> ok = default;
        Wrapper<T> x = default;
    }
}
