namespace Repro
{
    public struct Generic<T> { }
    public struct Vec<T> { }
    public struct S<T> { }
    public struct X { }
    public struct S_g1_bX_e { }
    public class Holder<T> { public struct Inner { } }
}
public struct GlobalType { }
namespace Functions
{
    public static class G
    {
        public static void Scale<T>(T[] a) { }
        public static T Pick<T>(T a) => a;
        public static T Pick<T>(T a, T b) => b;
        public static void Convert<TFrom, TTo>(TFrom[] a, TTo[] b) { }

        // Plain methods that the tests register as the closed copies of the generic ones
        public static T2 Open<T2>(T2 a) => a;
        public static int PickCopy(int a, int b) => b;
        public static void ScaleCopy(int[] a) { }
    }

    public static class Outer<T>
    {
        public static class Kernels
        {
            public static void Run(T[] a) { }
            public static void Map<U>(T[] a, U[] b) { }
        }
    }
}
