// RS2: a GpuMath view (ref struct) used across await. C# 13 allows ref struct locals in async methods, but not across an
// await; the error is reported when the async method is lowered, so GpuMathViewEscapeTests emits this file.
using System.Threading.Tasks;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

namespace GpuMathViewEscapeAsyncCases;

public static class Escapes
{
    public static async Task AcrossAwait(GpuBigUIntArray array)
    {
        var view = array.GetElement(0);
        await Task.Yield();
        _ = view.IsZero();
    }
}
