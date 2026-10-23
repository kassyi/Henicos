namespace Repro
{
    public struct A { public struct B { public struct C { } public struct B_C { } } }
    public struct Plain { }
    public class Outer<T> { public struct Inner<U> { } }
    public struct Generic<T> { }
    public struct Generic<T, U> { }
}
namespace N1 { public struct Outer { public struct Inner { } } }
namespace N2 { public struct Outer_Inner { } }
public struct GlobalType { }
namespace Functions
{
    public static class A { public static int Foo(int x) => x; }
    public static class B
    {
        public static int Foo(long x) => 0;
        public static int Pick(double x) => 1;
        public static int Pick(long x) => 2;
        public static int Pick(int x, int y) => 3;
    }
    public static class K
    {
        public static void Run(int[] a) { }
        public static void Run(float[] a) { }
        public static void Single(int[] a) { }
    }
    public static class Entry_Host { public static void RunKernel(int[] a) { } }
    public static class Entry { public static void Host_RunKernel(int[] a) { } }
}
