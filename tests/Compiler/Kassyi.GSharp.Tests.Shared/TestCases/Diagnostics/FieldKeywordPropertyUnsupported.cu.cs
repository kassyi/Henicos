using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct FieldKeywordMacroContext
{
    public int Calls;
}

public struct FieldKeywordMacroValue
{
    public int V;

    [GpuOperatorMacro(typeof(FieldKeywordMacroValue), nameof(Add))]
    public static FieldKeywordMacroValue operator +(FieldKeywordMacroValue left, FieldKeywordMacroValue right) =>
        throw new GpuOnlyException();

    [GpuDeviceFunction]
    private static void Add(ref FieldKeywordMacroContext ctx, ref FieldKeywordMacroValue result,
        FieldKeywordMacroValue left, FieldKeywordMacroValue right)
    {
        result.V = left.V + right.V;
        ctx.Calls = ctx.Calls + 1;
    }
}

// An operator macro writes its result through a reference, which would skip the setter
public struct FieldKeywordMacroHolder
{
    public FieldKeywordMacroValue P { get; set => field = new FieldKeywordMacroValue { V = value.V * 10 }; }
}

public static partial class FieldKeywordPropertyUnsupportedKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MacroTargetKernel(int[] data)
    {
        FieldKeywordMacroContext ctx = default;
        var holder = new FieldKeywordMacroHolder();
        var a = new FieldKeywordMacroValue { V = data[0] };
        holder.P = a + a;
        data[0] = holder.P.V + ctx.Calls;
    }
}
