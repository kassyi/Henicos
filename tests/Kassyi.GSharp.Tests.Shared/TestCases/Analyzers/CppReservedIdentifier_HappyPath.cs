using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct GSH_57_PlainStruct
{
    public int Value;
}

// Only used from host code, so its C++-reserved field names are never emitted.
public struct GSH_57_HostOnlyStruct
{
    public int register;
    public int __gsharp_value;
}

public static partial class GSH_57_CppReservedIdentifier_HappyPath
{
    [GpuKernel]
    public static void OrdinaryNamesKernel(int[] data, GSH_57_PlainStruct[] items)
    {
        int value = data[ThreadIndex.X];
        int @value2 = value + items[ThreadIndex.X].Value;
        int _single = @value2;
        int snake_case = _single;
        data[ThreadIndex.X] = snake_case;
    }

    public static int HostOnly(GSH_57_HostOnlyStruct s) => s.register + s.__gsharp_value;
}
