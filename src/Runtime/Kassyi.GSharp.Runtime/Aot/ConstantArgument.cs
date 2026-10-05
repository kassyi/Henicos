
namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Specifies the primitive data type of a JIT constant argument.
/// </summary>
public enum ConstantType : byte
{
    /// <summary>32-bit signed integer.</summary>
    Int32,
    /// <summary>32-bit unsigned integer.</summary>
    UInt32,
    /// <summary>64-bit signed integer.</summary>
    Int64,
    /// <summary>64-bit unsigned integer.</summary>
    UInt64,
    /// <summary>Single-precision floating point number.</summary>
    Single,
    /// <summary>Double-precision floating point number.</summary>
    Double,
    /// <summary>Boolean value.</summary>
    Boolean
}

/// <summary>
/// Represents an unboxed union value for a JIT constant argument.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct ConstantValue
{
    /// <summary>The 32-bit signed integer value.</summary>
    [FieldOffset(0)] public int Int32Value;
    /// <summary>The 32-bit unsigned integer value.</summary>
    [FieldOffset(0)] public uint UInt32Value;
    /// <summary>The 64-bit signed integer value.</summary>
    [FieldOffset(0)] public long Int64Value;
    /// <summary>The 64-bit unsigned integer value.</summary>
    [FieldOffset(0)] public ulong UInt64Value;
    /// <summary>The single-precision floating-point value.</summary>
    [FieldOffset(0)] public float SingleValue;
    /// <summary>The double-precision floating-point value.</summary>
    [FieldOffset(0)] public double DoubleValue;
    /// <summary>The boolean value.</summary>
    [FieldOffset(0)] public bool BooleanValue;
}

/// <summary>
/// Represents a key-value constant argument passed to JIT kernel compilation without heap allocation or boxing.
/// </summary>
public readonly struct ConstantArgument
{
    /// <summary>Gets the name of the constant template parameter.</summary>
    public readonly string Key;
    /// <summary>Gets the data type of the constant argument.</summary>
    public readonly ConstantType Type;
    /// <summary>Gets the unboxed value union of the constant argument.</summary>
    public readonly ConstantValue Value;

    private ConstantArgument(string key, ConstantType type, ConstantValue value)
    {
        Key = key;
        Type = type;
        Value = value;
    }

    // ReSharper disable UnusedMember.Global
    /// <summary>Creates a new constant argument for a 32-bit signed integer.</summary>
    /// <param name="key">The parameter name.</param>
    /// <param name="value">The integer value.</param>
    /// <returns>A new <see cref="ConstantArgument"/> instance.</returns>
    public static ConstantArgument Create(string key, int value) =>
        new(key, ConstantType.Int32, new ConstantValue { Int32Value = value });

    /// <summary>Creates a new constant argument for a 32-bit unsigned integer.</summary>
    /// <param name="key">The parameter name.</param>
    /// <param name="value">The unsigned integer value.</param>
    /// <returns>A new <see cref="ConstantArgument"/> instance.</returns>
    public static ConstantArgument Create(string key, uint value) =>
        new(key, ConstantType.UInt32, new ConstantValue { UInt32Value = value });

    /// <summary>Creates a new constant argument for a 64-bit signed integer.</summary>
    /// <param name="key">The parameter name.</param>
    /// <param name="value">The long integer value.</param>
    /// <returns>A new <see cref="ConstantArgument"/> instance.</returns>
    public static ConstantArgument Create(string key, long value) =>
        new(key, ConstantType.Int64, new ConstantValue { Int64Value = value });

    /// <summary>Creates a new constant argument for a 64-bit unsigned integer.</summary>
    /// <param name="key">The parameter name.</param>
    /// <param name="value">The unsigned long integer value.</param>
    /// <returns>A new <see cref="ConstantArgument"/> instance.</returns>
    public static ConstantArgument Create(string key, ulong value) =>
        new(key, ConstantType.UInt64, new ConstantValue { UInt64Value = value });

    /// <summary>Creates a new constant argument for a single-precision floating point number.</summary>
    /// <param name="key">The parameter name.</param>
    /// <param name="value">The float value.</param>
    /// <returns>A new <see cref="ConstantArgument"/> instance.</returns>
    public static ConstantArgument Create(string key, float value) =>
        new(key, ConstantType.Single, new ConstantValue { SingleValue = value });

    /// <summary>Creates a new constant argument for a double-precision floating point number.</summary>
    /// <param name="key">The parameter name.</param>
    /// <param name="value">The double value.</param>
    /// <returns>A new <see cref="ConstantArgument"/> instance.</returns>
    public static ConstantArgument Create(string key, double value) =>
        new(key, ConstantType.Double, new ConstantValue { DoubleValue = value });

    /// <summary>Creates a new constant argument for a boolean value.</summary>
    /// <param name="key">The parameter name.</param>
    /// <param name="value">The boolean value.</param>
    /// <returns>A new <see cref="ConstantArgument"/> instance.</returns>
    public static ConstantArgument Create(string key, bool value) =>
        new(key, ConstantType.Boolean, new ConstantValue { BooleanValue = value });

    /// <summary>
    /// Converts the constant argument to its string representation used during JIT code generation.
    /// </summary>
    /// <returns>A string representation formatted for CUDA/C++ template substitution.</returns>
    public string GetStringValue()
    {
        return Type switch
        {
            ConstantType.Int32 => Value.Int32Value.ToString(),
            ConstantType.UInt32 => Value.UInt32Value.ToString(),
            ConstantType.Int64 => Value.Int64Value.ToString(),
            ConstantType.UInt64 => Value.UInt64Value.ToString(),
            ConstantType.Single => Value.SingleValue.ToString("R"),
            ConstantType.Double => Value.DoubleValue.ToString("R"),
            ConstantType.Boolean => Value.BooleanValue ? "true" : "false",
            _ => string.Empty
        };
    }
    // ReSharper restore UnusedMember.Global
}
