using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Core;

public static partial class ManagedGpuContext
{
    // The managed path of an int or long division that may divide the minimum by -1 (#718): the GPU gives MinValue for
    // MinValue / -1 and 0 for MinValue % -1, where .NET throws OverflowException, and G# gives the GPU result on every
    // path. Any other divisor is the C# operator, which throws DivideByZeroException for 0. The generated managed code
    // calls these only where the compiler cannot rule the case out; every other division stays the C# operator.

    /// <summary>
    /// The quotient of <paramref name="a"/> by <paramref name="b"/>, <c>MinValue / -1</c> giving MinValue.
    /// </summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The quotient, rounded toward zero.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int __gsharp_div_i32(int a, int b) => b == -1 ? unchecked(-a) : a / b;

    /// <summary>
    /// The quotient of <paramref name="a"/> by <paramref name="b"/>, <c>MinValue / -1</c> giving MinValue.
    /// </summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The quotient, rounded toward zero.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long __gsharp_div_i64(long a, long b) => b == -1 ? unchecked(-a) : a / b;

    /// <summary>
    /// The remainder of <paramref name="a"/> by <paramref name="b"/>, <c>MinValue % -1</c> giving 0.
    /// </summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The remainder, with the sign of the dividend.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int __gsharp_rem_i32(int a, int b) => b == -1 ? 0 : a % b;

    /// <summary>
    /// The remainder of <paramref name="a"/> by <paramref name="b"/>, <c>MinValue % -1</c> giving 0.
    /// </summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The remainder, with the sign of the dividend.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long __gsharp_rem_i64(long a, long b) => b == -1 ? 0 : a % b;

    /// <summary><c>target /= b</c>, <c>MinValue / -1</c> giving MinValue.</summary>
    /// <param name="target">The variable divided and assigned.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The value assigned.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int __gsharp_div_assign_i32(ref int target, int b) => target = __gsharp_div_i32(target, b);

    /// <summary><c>target /= b</c>, <c>MinValue / -1</c> giving MinValue.</summary>
    /// <param name="target">The variable divided and assigned.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The value assigned.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long __gsharp_div_assign_i64(ref long target, long b) => target = __gsharp_div_i64(target, b);

    /// <summary><c>target %= b</c>, <c>MinValue % -1</c> giving 0.</summary>
    /// <param name="target">The variable divided and assigned.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The value assigned.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int __gsharp_rem_assign_i32(ref int target, int b) => target = __gsharp_rem_i32(target, b);

    /// <summary><c>target %= b</c>, <c>MinValue % -1</c> giving 0.</summary>
    /// <param name="target">The variable divided and assigned.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The value assigned.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long __gsharp_rem_assign_i64(ref long target, long b) => target = __gsharp_rem_i64(target, b);

    // A compound assignment of a location in memory whose value writes it (a[i] /= f(), where f writes a[i]): C# reads
    // the target before it evaluates the value. The reader finds the location once and reads it; the store then divides
    // the value read and stores into the same location:
    // __gsharp_div_store_i32(ref __gsharp_read_ref_i32(ref a[i], out var c), c, f())

    /// <summary>Reads <paramref name="target"/> into <paramref name="current"/> and returns the same location.</summary>
    /// <param name="target">The location of the compound assignment.</param>
    /// <param name="current">The value of the location before the value of the assignment runs.</param>
    /// <returns><paramref name="target"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref int __gsharp_read_ref_i32(ref int target, out int current)
    {
        current = target;
        return ref target;
    }

    /// <summary>Reads <paramref name="target"/> into <paramref name="current"/> and returns the same location.</summary>
    /// <param name="target">The location of the compound assignment.</param>
    /// <param name="current">The value of the location before the value of the assignment runs.</param>
    /// <returns><paramref name="target"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref long __gsharp_read_ref_i64(ref long target, out long current)
    {
        current = target;
        return ref target;
    }

    /// <summary>
    /// Stores <c>current / b</c> into <paramref name="target"/>, <c>MinValue / -1</c> giving MinValue.
    /// </summary>
    /// <param name="target">The location of the compound assignment.</param>
    /// <param name="current">The value the location held before the divisor was evaluated.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The value stored.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int __gsharp_div_store_i32(ref int target, int current, int b) =>
        target = __gsharp_div_i32(current, b);

    /// <summary>
    /// Stores <c>current / b</c> into <paramref name="target"/>, <c>MinValue / -1</c> giving MinValue.
    /// </summary>
    /// <param name="target">The location of the compound assignment.</param>
    /// <param name="current">The value the location held before the divisor was evaluated.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The value stored.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long __gsharp_div_store_i64(ref long target, long current, long b) =>
        target = __gsharp_div_i64(current, b);

    /// <summary>Stores <c>current % b</c> into <paramref name="target"/>, <c>MinValue % -1</c> giving 0.</summary>
    /// <param name="target">The location of the compound assignment.</param>
    /// <param name="current">The value the location held before the divisor was evaluated.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The value stored.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int __gsharp_rem_store_i32(ref int target, int current, int b) =>
        target = __gsharp_rem_i32(current, b);

    /// <summary>Stores <c>current % b</c> into <paramref name="target"/>, <c>MinValue % -1</c> giving 0.</summary>
    /// <param name="target">The location of the compound assignment.</param>
    /// <param name="current">The value the location held before the divisor was evaluated.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The value stored.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long __gsharp_rem_store_i64(ref long target, long current, long b) =>
        target = __gsharp_rem_i64(current, b);

    // The compound assignment of a property whose receiver is found once and read before the value (the receiver or an
    // argument writes, may throw, or the value writes what it reads): the getter and the setter take the receiver, which
    // the reader hands on. A value type goes through a reference to its variable, a reference type by its object.
    // __gsharp_div_property_store(ref __gsharp_read_property(ref h, static (ref H r) => r.P, out var c), c, v,
    //     static (ref H r, int x) => r.P = x)

    /// <summary>Reads a property of the variable <paramref name="receiver"/>; the getter of a value type.</summary>
    /// <typeparam name="TReceiver">The type of the variable.</typeparam>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    /// <param name="receiver">The variable the property belongs to.</param>
    /// <param name="get">The getter.</param>
    /// <param name="current">The value of the property before the value of the assignment runs.</param>
    /// <returns><paramref name="receiver"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ref TReceiver __gsharp_read_property<TReceiver, TValue>(
        ref TReceiver receiver, ManagedRefGetter<TReceiver, TValue> get, out TValue current)
    {
        current = get(ref receiver);
        return ref receiver;
    }

    /// <summary>Reads a property of the object <paramref name="receiver"/>.</summary>
    /// <typeparam name="TReceiver">The type of the object.</typeparam>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    /// <param name="receiver">The object the property belongs to.</param>
    /// <param name="get">The getter.</param>
    /// <param name="current">The value of the property before the value of the assignment runs.</param>
    /// <returns><paramref name="receiver"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TReceiver __gsharp_read_property_of<TReceiver, TValue>(
        TReceiver receiver, Func<TReceiver, TValue> get, out TValue current)
    {
        current = get(receiver);
        return receiver;
    }

    /// <summary>Sets the property to <c>current / b</c>, <c>MinValue / -1</c> giving MinValue.</summary>
    /// <typeparam name="TReceiver">The type of the variable.</typeparam>
    /// <typeparam name="TValue">The type of the property (int or long).</typeparam>
    /// <param name="receiver">The variable the property belongs to.</param>
    /// <param name="current">The value the property held before the divisor was evaluated.</param>
    /// <param name="b">The divisor.</param>
    /// <param name="set">The setter.</param>
    /// <returns>The value stored.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TValue __gsharp_div_property_store<TReceiver, TValue>(
        ref TReceiver receiver, TValue current, TValue b, ManagedRefSetter<TReceiver, TValue> set)
        where TValue : IBinaryInteger<TValue>, ISignedNumber<TValue>
    {
        var result = b == TValue.NegativeOne ? unchecked(-current) : current / b;
        set(ref receiver, result);
        return result;
    }

    /// <summary>Sets the property to <c>current % b</c>, <c>MinValue % -1</c> giving 0.</summary>
    /// <typeparam name="TReceiver">The type of the variable.</typeparam>
    /// <typeparam name="TValue">The type of the property (int or long).</typeparam>
    /// <param name="receiver">The variable the property belongs to.</param>
    /// <param name="current">The value the property held before the divisor was evaluated.</param>
    /// <param name="b">The divisor.</param>
    /// <param name="set">The setter.</param>
    /// <returns>The value stored.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TValue __gsharp_rem_property_store<TReceiver, TValue>(
        ref TReceiver receiver, TValue current, TValue b, ManagedRefSetter<TReceiver, TValue> set)
        where TValue : IBinaryInteger<TValue>, ISignedNumber<TValue>
    {
        var result = b == TValue.NegativeOne ? TValue.Zero : current % b;
        set(ref receiver, result);
        return result;
    }

    /// <summary>Sets the property of the object to <c>current / b</c>, <c>MinValue / -1</c> giving MinValue.</summary>
    /// <typeparam name="TReceiver">The type of the object.</typeparam>
    /// <typeparam name="TValue">The type of the property (int or long).</typeparam>
    /// <param name="receiver">The object the property belongs to.</param>
    /// <param name="current">The value the property held before the divisor was evaluated.</param>
    /// <param name="b">The divisor.</param>
    /// <param name="set">The setter.</param>
    /// <returns>The value stored.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TValue __gsharp_div_property_store_of<TReceiver, TValue>(
        TReceiver receiver, TValue current, TValue b, Action<TReceiver, TValue> set)
        where TValue : IBinaryInteger<TValue>, ISignedNumber<TValue>
    {
        var result = b == TValue.NegativeOne ? unchecked(-current) : current / b;
        set(receiver, result);
        return result;
    }

    /// <summary>Sets the property of the object to <c>current % b</c>, <c>MinValue % -1</c> giving 0.</summary>
    /// <typeparam name="TReceiver">The type of the object.</typeparam>
    /// <typeparam name="TValue">The type of the property (int or long).</typeparam>
    /// <param name="receiver">The object the property belongs to.</param>
    /// <param name="current">The value the property held before the divisor was evaluated.</param>
    /// <param name="b">The divisor.</param>
    /// <param name="set">The setter.</param>
    /// <returns>The value stored.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TValue __gsharp_rem_property_store_of<TReceiver, TValue>(
        TReceiver receiver, TValue current, TValue b, Action<TReceiver, TValue> set)
        where TValue : IBinaryInteger<TValue>, ISignedNumber<TValue>
    {
        var result = b == TValue.NegativeOne ? TValue.Zero : current % b;
        set(receiver, result);
        return result;
    }
}

/// <summary>The getter of a property of a value type, which takes the variable it belongs to by reference.</summary>
/// <typeparam name="TReceiver">The type of the variable.</typeparam>
/// <typeparam name="TValue">The type of the property.</typeparam>
/// <param name="receiver">The variable.</param>
/// <returns>The value of the property.</returns>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate TValue ManagedRefGetter<TReceiver, TValue>(ref TReceiver receiver);

/// <summary>The setter of a property of a value type, which takes the variable it belongs to by reference.</summary>
/// <typeparam name="TReceiver">The type of the variable.</typeparam>
/// <typeparam name="TValue">The type of the property.</typeparam>
/// <param name="receiver">The variable.</param>
/// <param name="value">The value to set.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void ManagedRefSetter<TReceiver, TValue>(ref TReceiver receiver, TValue value);
