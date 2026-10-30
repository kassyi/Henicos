using Kassyi.GSharp.Core;

namespace ManagedOperatorMacroCase;

public struct FormValue
{
    public int V;

    public FormValue(int v) => V = v;

    [GpuOperatorMacro(typeof(FormOps), nameof(FormOps.Add))]
    public static FormValue operator +(FormValue left, FormValue right) => throw new GpuOnlyException();
}

public struct FormHolder
{
    public FormValue P { get; set; }

    public FormValue Field;
}

public static partial class FormOps
{
    [GpuDeviceFunction]
    public static void Add(ref FormValue result, in FormValue left, in FormValue right)
    {
        result.V = left.V + right.V;
    }

    [GpuDeviceFunction]
    public static FormHolder Make(int v)
    {
        FormHolder h = default;
        h.Field = new FormValue(v);
        h.P = new FormValue(v);
        return h;
    }

    [GpuDeviceFunction]
    public static ref FormValue At(FormValue[] values, int index) => ref values[index];
}

// The forms of operands and targets that the managed expansion passes by reference, or as values when they have no
// storage (#711): the generated managed code must compile
public static partial class ManagedOperatorMacroFormKernels
{
    [GpuKernel]
    public static void FormKernel(FormValue[] values, int[] output)
    {
        FormHolder h = default;
        FormValue c = default;
        FormValue d = new FormValue(1);
        h.P = c + d;
        h.P = h.P + d;
        c = FormOps.Make(5).Field + d;
        c = FormOps.Make(6).P + d;
        c = FormOps.At(values, 0) + d;
        output[0] = c.V + h.P.V + InFunction(d);
    }

    [GpuDeviceFunction]
    public static int InFunction(FormValue d)
    {
        FormHolder h = default;
        h.P = d + d;
        h.Field = FormOps.Make(2).Field + h.P;
        return h.Field.V;
    }
}
