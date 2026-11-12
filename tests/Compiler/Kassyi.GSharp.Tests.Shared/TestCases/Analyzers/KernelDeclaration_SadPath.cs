using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

// Every [GpuKernel] here is on a declaration that cannot be a kernel (GSH0111), except the ones after the comment below
public partial struct KernelDeclarationValue
{
    public int X;

    [GpuKernel]
    public static KernelDeclarationValue operator +(KernelDeclarationValue a, KernelDeclarationValue b) => a;

    [GpuKernel]
    public static implicit operator int(KernelDeclarationValue a) => a.X;

    [GpuKernel]
    public static void StaticInStruct(int[] o) { o[0] = 42; }

    [GpuKernel]
    public void InstanceInStruct(int[] o) { o[0] = X; }
}

public ref partial struct KernelDeclarationScope
{
    [GpuKernel]
    public void Dispose() { }
}

public partial interface IKernelDeclarationShape
{
    [GpuKernel]
    static abstract void Abstract(int[] o);

    [GpuKernel]
    void DefaultBody(int[] o) { o[0] = 42; }
}

public partial class KernelDeclarationMembers
{
    public static int Value { [GpuKernel] get { return 42; } }

    public int this[int i] { [GpuKernel] get { return i; } }

    [GpuKernel]
    public void Instance(int[] o) { o[0] = 42; }

    [GpuKernel]
    public virtual void Virtual(int[] o) { o[0] = 42; }

    [GpuKernel, ModuleInitializer]
    public static void Init() { }

    [GpuKernel, UnmanagedCallersOnly]
    public static void Native(int o) { }

    [GpuKernel]
    public static void Generic<T>(T[] o) where T : unmanaged { }

    public static void Host(int[] o)
    {
        var lambda = [GpuKernel] (int[] p) => { p[0] = 42; };
        LocalKernel(o);

        [GpuKernel]
        static void LocalKernel(int[] p) { p[0] = 42; }
    }
}

public static partial class KernelDeclarationGeneric<T>
{
    [GpuKernel]
    public static void InGenericType(int[] o) { o[0] = 42; }
}

public interface IKernelDeclarationRun
{
    static abstract void Run(int[] o);
}

public partial class KernelDeclarationImplementation : IKernelDeclarationRun
{
    [GpuKernel]
    public static void Run(int[] o) { o[0] = 42; }
}

public static partial class KernelDeclarationPatterns
{
    [GpuKernel]
    public static void Deconstruct(this int[] o, out int a, out int b) { a = o[0]; b = o[1]; }

    [GpuKernel]
    public static void Add(this KernelDeclarationCollection c, int value) { }

    [GpuKernel]
    public static KernelDeclarationCollection Create(ReadOnlySpan<int> items) => default;
}

[CollectionBuilder(typeof(KernelDeclarationPatterns), "Create")]
public struct KernelDeclarationCollection
{
    public int Count;

    public readonly ReadOnlySpan<int>.Enumerator GetEnumerator() => default;
}

// Ordinary static methods of a class: no GSH0111
public static partial class KernelDeclarationValid
{
    [GpuKernel]
    public static void Ordinary(int[] o) { o[0] = 42; }

    [GpuKernel]
    public static void Fill(this int[] o) { o[0] = 42; }

    [GpuKernel]
    public static void Create(ReadOnlySpan<int> items, int[] o) { o[0] = items[0]; }
}

public partial struct KernelDeclarationOuter
{
    public static partial class Nested
    {
        [GpuKernel]
        public static void InNestedClass(int[] o) { o[0] = 42; }
    }
}

public partial record class KernelDeclarationRecord
{
    [GpuKernel]
    public static void InRecord(int[] o) { o[0] = 42; }
}
