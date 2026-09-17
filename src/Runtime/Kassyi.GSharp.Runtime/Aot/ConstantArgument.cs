
namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// JIT 定数の型を表します。
/// </summary>
public enum ConstantType : byte
{
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Boolean
}

/// <summary>
/// JIT 定数の値をボクシングなしで保持する共用体（Union）構造体。
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct ConstantValue
{
    [FieldOffset(0)] public int Int32Value;
    [FieldOffset(0)] public uint UInt32Value;
    [FieldOffset(0)] public long Int64Value;
    [FieldOffset(0)] public ulong UInt64Value;
    [FieldOffset(0)] public float SingleValue;
    [FieldOffset(0)] public double DoubleValue;
    [FieldOffset(0)] public bool BooleanValue;
}

/// <summary>
/// JIT コンパイルに渡される定数の名前と値のペア。
/// ボクシングやヒープ割り当てを避けるために使用されます。
/// </summary>
public readonly struct ConstantArgument
{
    public readonly string Key;
    public readonly ConstantType Type;
    public readonly ConstantValue Value;

    private ConstantArgument(string key, ConstantType type, ConstantValue value)
    {
        Key = key;
        Type = type;
        Value = value;
    }

    // ReSharper disable UnusedMember.Global
    public static ConstantArgument Create(string key, int value) =>
        new(key, ConstantType.Int32, new ConstantValue { Int32Value = value });

    public static ConstantArgument Create(string key, uint value) =>
        new(key, ConstantType.UInt32, new ConstantValue { UInt32Value = value });

    public static ConstantArgument Create(string key, long value) =>
        new(key, ConstantType.Int64, new ConstantValue { Int64Value = value });

    public static ConstantArgument Create(string key, ulong value) =>
        new(key, ConstantType.UInt64, new ConstantValue { UInt64Value = value });

    public static ConstantArgument Create(string key, float value) =>
        new(key, ConstantType.Single, new ConstantValue { SingleValue = value });

    public static ConstantArgument Create(string key, double value) =>
        new(key, ConstantType.Double, new ConstantValue { DoubleValue = value });

    public static ConstantArgument Create(string key, bool value) =>
        new(key, ConstantType.Boolean, new ConstantValue { BooleanValue = value });

    /// <summary>
    /// JIT 定数を文字列表現に変換します。キャッシュミス時のみ呼び出されます。
    /// </summary>
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
