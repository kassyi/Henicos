using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Jit;

/// <summary>
/// JIT 定数の型を表します。
/// </summary>
public enum JitConstantType : byte
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
public struct JitConstantValue
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
public readonly struct JitConstantArg
{
    public readonly string Key;
    public readonly JitConstantType Type;
    public readonly JitConstantValue Value;

    private JitConstantArg(string key, JitConstantType type, JitConstantValue value)
    {
        Key = key;
        Type = type;
        Value = value;
    }

    public static JitConstantArg Create(string key, int value) =>
        new(key, JitConstantType.Int32, new JitConstantValue { Int32Value = value });

    public static JitConstantArg Create(string key, uint value) =>
        new(key, JitConstantType.UInt32, new JitConstantValue { UInt32Value = value });

    public static JitConstantArg Create(string key, long value) =>
        new(key, JitConstantType.Int64, new JitConstantValue { Int64Value = value });

    public static JitConstantArg Create(string key, ulong value) =>
        new(key, JitConstantType.UInt64, new JitConstantValue { UInt64Value = value });

    public static JitConstantArg Create(string key, float value) =>
        new(key, JitConstantType.Single, new JitConstantValue { SingleValue = value });

    public static JitConstantArg Create(string key, double value) =>
        new(key, JitConstantType.Double, new JitConstantValue { DoubleValue = value });

    public static JitConstantArg Create(string key, bool value) =>
        new(key, JitConstantType.Boolean, new JitConstantValue { BooleanValue = value });

    /// <summary>
    /// JIT 定数を文字列表現に変換します。キャッシュミス時のみ呼び出されます。
    /// </summary>
    public string GetStringValue()
    {
        return Type switch
        {
            JitConstantType.Int32 => Value.Int32Value.ToString(),
            JitConstantType.UInt32 => Value.UInt32Value.ToString(),
            JitConstantType.Int64 => Value.Int64Value.ToString(),
            JitConstantType.UInt64 => Value.UInt64Value.ToString(),
            JitConstantType.Single => Value.SingleValue.ToString("R"),
            JitConstantType.Double => Value.DoubleValue.ToString("R"),
            JitConstantType.Boolean => Value.BooleanValue ? "true" : "false",
            _ => string.Empty
        };
    }
}
