namespace Cases;

public class Tiny
{
    public int Value;
}

public class Five
{
    public int A;
    public int B;
}

public class Mixed
{
    public int A, B;
    private const int C = 1;
    public event System.Action Changed1, Changed2;
    public Mixed() { }
    public int Property { get; set; }
    public int this[int index] => index;
    public static Mixed operator +(Mixed x, Mixed y) => x;
    public void Method() { }
    public class NestedClass { public int N; public int M; }
    public enum NestedEnum { One, Two }
    public delegate void NestedDelegate();
}

public enum Colors
{
    Red,
    Green,
    Blue,
}

public delegate void TopDelegate();

public interface IShape
{
    int Area();
    int Perimeter { get; }
}

public struct Point
{
    public int X;
    public int Y;
}

public record Person(string Name, int Age)
{
    public string Title => Name;
}

public record struct Pair(int A, int B);

public class Box<T>
{
    public T Item;
}

public class Outer
{
    public int Own;
    public class Inner
    {
        public int Deep;
    }
}

public static class Extras
{
    public static int Plain(string s) => s.Length;
    extension(string text)
    {
        public int Doubled() => text.Length * 2;
        public int Tripled => text.Length * 3;
    }
}
