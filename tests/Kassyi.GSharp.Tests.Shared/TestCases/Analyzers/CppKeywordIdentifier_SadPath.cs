using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct GSH_58_ReservedFieldStruct
{
    public int register;
}

public struct GSH_58_OuterStruct
{
    public GSH_58_ReservedFieldStruct Inner;
}

public static partial class GSH_58_CppKeywordIdentifier_SadPath
{
    [GpuKernel]
    public static void LocalKernel(int[] data)
    {
        int register = data[ThreadIndex.X];
        data[ThreadIndex.X] = register;
    }

    [GpuKernel]
    public static void ParameterKernel(int[] template)
    {
        template[ThreadIndex.X] = 0;
    }

    [GpuKernel]
    public static void VerbatimKernel(int[] data)
    {
        int @class = data[ThreadIndex.X];
        data[ThreadIndex.X] = @class;
    }

    [GpuKernel]
    public static void ContextualKeywordKernel(int[] data)
    {
        int not = data[ThreadIndex.X];
        data[ThreadIndex.X] = not;
    }

    [GpuKernel]
    public static void OutVarKernel(int[] data)
    {
        GetValue(out var union);
        data[ThreadIndex.X] = union;
    }

    [GpuDeviceFunction]
    public static void GetValue(out int value)
    {
        value = 1;
    }

    [GpuKernel]
    public static void LocalFunctionKernel(int[] data)
    {
        int mutable(int x) => x + 1;
        data[ThreadIndex.X] = mutable(data[ThreadIndex.X]);
    }

    [GpuKernel]
    public static void LambdaParameterKernel(int[] data)
    {
        Gpu.ForUnroll(0, 2, typename => { data[typename] = 0; });
    }

    [GpuKernel]
    public static void ForEachKernel(int[] data)
    {
        foreach (int and in data)
        {
            data[0] = and;
        }
    }

    [GpuDeviceFunction]
    public static int friend(int x) => x;

    [GpuKernel]
    public static void StructFieldKernel(GSH_58_ReservedFieldStruct[] items)
    {
        items[ThreadIndex.X].register = 1;
    }

    [GpuKernel]
    public static void NestedStructFieldKernel(int[] data)
    {
        GSH_58_OuterStruct outer = default;
        data[ThreadIndex.X] = outer.Inner.register;
    }
}
