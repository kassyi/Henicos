#nullable enable

using static Kassyi.GSharp.Tests.Shared.Instantiation.GenericCopyTestSupport;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>
/// Compiles the closed copies of generic declarations into an assembly and runs them next to the generic methods,
/// closed over the same type argument, with the inputs of the type.
/// </summary>
internal static class GenericCopyValueSupport
{
    // The smallest set of values of a type (nuint) that a method is run with
    private const int MinimumInputs = 9;

    // The declaration and the copy give the same value, or throw the same exception, for every input
    internal static void AssertCopyComputesWhatTheDeclarationComputes(string method, string typeArgument)
    {
        var loaded = Load(method, typeArgument);
        var arity = loaded.Generic.GetParameters().Length;

        var compared = 0;
        foreach (var arguments in Inputs(loaded.Type, arity, loaded.TakesCount))
        {
            Assert.Equal(Run(loaded.Generic, arguments), Run(loaded.Closed, arguments));
            compared++;
        }

        Assert.True(compared >= (arity == 0 ? 1 : MinimumInputs), $"only {compared} inputs");
    }

    // The generic method closed over the type argument, and the closed copy, of an assembly compiled from the copies
    internal static LoadedMethods Load(string method, string typeArgument)
    {
        var (copy, set) = CreateCopy(method, typeArgument);
        Assert.True(copy.IsFaithful, Describe(copy));
        var copyName = Assert.IsAssignableFrom<IMethodSymbol>(copy.Symbol).Name;
        using var stream = new MemoryStream();
        var emitted = set.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

#pragma warning disable RS0030 // Reflection calls an assembly this test compiled at run time
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var kernels = assembly.GetType("App.Kernels") ??
                      throw new InvalidOperationException("No App.Kernels in the emitted assembly.");
        var type = Type.GetType(typeArgument) ?? assembly.GetType(typeArgument) ??
                   throw new InvalidOperationException(typeArgument);
        var definition = kernels.GetMethod(method) ?? throw new InvalidOperationException(method);
        var closed = kernels.GetMethod(copyName) ?? throw new InvalidOperationException(copyName);
        // A second parameter that is not of the type argument is a shift count
        var takesCount = definition.GetParameters() is [_, { ParameterType.IsGenericParameter: false }];
        return new LoadedMethods(type, definition.MakeGenericMethod(type), closed, takesCount);
#pragma warning restore RS0030
    }

    internal static object CreateDefault(Type type)
    {
#pragma warning disable RS0030 // Reflection creates a value of a type this test compiled at run time
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException(type.Name);
#pragma warning restore RS0030
    }

    // Two default values of the type, in an array
    internal static Array CreateArray(Type type)
    {
        var items = Array.CreateInstance(type, 2);
        items.SetValue(CreateDefault(type), 0);
        items.SetValue(CreateDefault(type), 1);
        return items;
    }

    internal sealed record LoadedMethods(
        Type Type,
        System.Reflection.MethodInfo Generic,
        System.Reflection.MethodInfo Closed,
        bool TakesCount);

    internal static string Run(System.Reflection.MethodBase method, object[] arguments)
    {
        try
        {
#pragma warning disable RS0030 // Reflection calls an assembly this test compiled at run time
            return method.Invoke(null, arguments)?.ToString() ?? "null";
#pragma warning restore RS0030
        }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is { } inner)
        {
            return inner.GetType().Name;
        }
    }

    // Every value of a byte-sized type, a spread of the others; shift counts from -40 to 40
    internal static IEnumerable<object[]> Inputs(Type type, int arity, bool takesCount)
    {
        if (arity == 0)
        {
            return [[]];
        }

        var values = Values(type).ToArray();
        if (arity == 1)
        {
            return values.Select(v => new[] { v });
        }

        var second = takesCount ? Enumerable.Range(-40, 81).Select(n => (object)n).ToArray()
            : values.Length <= 300 ? values
            : values.Where((_, i) => i % 97 == 0).ToArray();
        return values.SelectMany(a => second.Select(b => new[] { a, b }));
    }

    internal static IEnumerable<object> Values(Type type) =>
        SmallIntegerValues(type) ?? UnsignedValues(type) ?? SignedValues(type) ?? NativeIntegerValues(type) ??
        RealValues(type) ?? throw new InvalidOperationException($"No inputs for {type}.");

    // Every value of a byte-sized type, a spread of the 16-bit ones; null for another type
    private static IEnumerable<object>? SmallIntegerValues(Type type)
    {
        if (type == typeof(byte))
        {
            return Enumerable.Range(0, 256).Select(i => (object)(byte)i);
        }

        if (type == typeof(sbyte))
        {
            return Enumerable.Range(-128, 256).Select(i => (object)(sbyte)i);
        }

        if (type == typeof(short))
        {
            return Spread().Select(i => (object)(short)i);
        }

        if (type == typeof(ushort))
        {
            return Spread().Select(i => (object)(ushort)i);
        }

        return type == typeof(char) ? Spread().Select(i => (object)(char)i) : null;
    }

    // The corners of uint and ulong, and a spread; null for another type
    private static IEnumerable<object>? UnsignedValues(Type type)
    {
        if (type == typeof(uint))
        {
            return new uint[] { 0, 1, 2, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFE, 0xFFFFFFFF }.Select(v => (object)v)
                .Concat(Enumerable.Range(0, 200).Select(i => (object)(uint)(i * 21474836)));
        }

        return type == typeof(ulong)
            ? new ulong[] { 0, 1, 2, long.MaxValue, 1UL << 63, ulong.MaxValue }.Select(v => (object)v)
                .Concat(Enumerable.Range(0, 200).Select(i => (object)(ulong)(i * 92233720368547758L)))
            : null;
    }

    // The corners of int and long; null for another type
    private static IEnumerable<object>? SignedValues(Type type)
    {
        if (type == typeof(int))
        {
            return new[]
            {
                int.MinValue, -123456789, -32769, -32768, -129, -128, -2, -1, 0, 1, 2, 7, 8, 9, 31, 32, 33, 127, 128,
                255, 256, 32767, 32768, 65535, 65536, 123456789, int.MaxValue - 1, int.MaxValue,
            }.Select(v => (object)v);
        }

        return type == typeof(long)
            ? new[] { long.MinValue, int.MinValue - 1L, -2L, -1L, 0L, 1L, 2L, 63L, 64L, 65L, int.MaxValue + 1L,
                long.MaxValue - 1, long.MaxValue }.Select(v => (object)v)
            : null;
    }

    // The corners of nint and nuint; null for another type
    private static IEnumerable<object>? NativeIntegerValues(Type type)
    {
        if (type == typeof(nint))
        {
            return new nint[] { nint.MinValue, -2, -1, 0, 1, 2, 63, 64, 65, nint.MaxValue - 1, nint.MaxValue }
                .Select(v => (object)v);
        }

        return type == typeof(nuint)
            ? new nuint[] { 0, 1, 2, 63, 64, 65, nuint.MaxValue / 2, nuint.MaxValue - 1, nuint.MaxValue }
                .Select(v => (object)v)
            : null;
    }

    // NaN, the infinities, signed zero and the corners; null for another type
    private static IEnumerable<object>? RealValues(Type type)
    {
        if (type == typeof(float))
        {
            return new[]
            {
                float.NaN, float.NegativeInfinity, float.MinValue, -1.5f, -1f, -0f, 0f, float.Epsilon, 0.5f, 1f, 2f,
                7.25f, float.MaxValue, float.PositiveInfinity,
            }.Select(v => (object)v);
        }

        if (type == typeof(double))
        {
            return new[]
            {
                double.NaN, double.NegativeInfinity, double.MinValue, -1.5, -1d, -0d, 0d, double.Epsilon, 0.5, 1d, 2d,
                7.25, double.MaxValue, double.PositiveInfinity,
            }.Select(v => (object)v);
        }

        return type == typeof(decimal)
            ? new[] { decimal.MinValue, -1.5m, -1m, 0m, 0.5m, 1m, 2m, 7.25m, decimal.MaxValue }.Select(v => (object)v)
            : null;
    }

    // Every 61st value of a 16-bit type, and its edges
    internal static IEnumerable<int> Spread() =>
        Enumerable.Range(0, 65536).Where(i => i % 61 == 0 || i < 8 || i > 65528 || i is >= 32760 and <= 32775);
}
