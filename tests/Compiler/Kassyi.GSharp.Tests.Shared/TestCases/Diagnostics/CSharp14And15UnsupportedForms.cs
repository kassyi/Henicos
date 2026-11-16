#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases
{
    [CollectionBuilder(typeof(Scaled3Builder), nameof(Scaled3Builder.Create))]
    public struct Scaled3
    {
        public int A;
        public int B;
        public int C;
        public Scaled3(int a, int b, int c) { A = a; B = b; C = c; }
        public readonly IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
    }

    public static partial class Scaled3Builder
    {
        [GpuDeviceFunction]
        public static Scaled3 Create(int scale, ReadOnlySpan<int> items) =>
            new Scaled3(items[0] * scale, items[1] * scale, items[2] * scale);
    }

    public record struct Cat(int Lives);
    public record struct Dog(int Barks);
    public union Pet(Cat, Dog);

    public closed class Shape { }
    public sealed class Square : Shape { }
    public sealed class Circle : Shape { }

    public struct Acc
    {
        public int V;
        public void operator +=(int x) => V += x * 10;

        public static Acc operator +(Acc a, int x) => new Acc { V = a.V + x };
    }

    public static partial class CSharp14And15UnsupportedForms
    {
        [GpuKernel]
        public static void WithElementKernel(int[] o)
        {
            int t = ThreadIndex.X;
            Scaled3 s = [with(t), 1, 2, 3];
            o[t] = s.A + s.B * 10 + s.C * 100;
        }

        [GpuKernel]
        public static void UnionKernel(int[] o)
        {
            int t = ThreadIndex.X;
            Pet p = t % 2 == 0 ? new Pet(new Cat(t)) : new Pet(new Dog(t * 2));
            o[t] = p switch { Cat c => c.Lives, Dog d => d.Barks };
        }

        [GpuKernel]
        public static void ClosedKernel(int[] o)
        {
            int t = ThreadIndex.X;
            Shape s = t % 2 == 0 ? new Square() : new Circle();
            o[t] = s switch { Square => 1, Circle => 2 };
        }

        [GpuKernel]
        public static void CompoundKernel(int[] o)
        {
            int t = ThreadIndex.X;
            Acc a = default;
            a += t;
            a += 1;
            Acc b = a + 5;
            o[t] = a.V * 1000 + b.V;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class UnionAttribute : Attribute { }

    public interface IUnion
    {
        object? Value { get; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class IsClosedTypeAttribute : Attribute { }
}
