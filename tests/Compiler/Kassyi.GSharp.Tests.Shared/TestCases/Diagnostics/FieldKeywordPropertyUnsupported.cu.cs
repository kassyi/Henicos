using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// A constructor may step a property without a setter whose getter uses the field keyword: C# reads the getter and
// writes the backing field, which G# reports instead of guessing
public struct FieldKeywordStepped
{
    public FieldKeywordStepped(int v)
    {
        R = v;
        R += 1;
    }

    public int R { get => field * 3; }
}

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
    public static void SteppedKernel(int[] data)
    {
        var s = new FieldKeywordStepped(data[0]);
        data[0] = s.R;
    }

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
