namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Max and Min return one of the operands, its sign flag included (-0 stays -0). Operands of the same host sign, or a
// device sign, are compared on the device (SignedDeviceOps.Select); different host signs decide the operand on the host
// (SignPlan.Choose), which is copied without a comparison.
internal static class SignedSelect
{
    // The chosen magnitude in resultBuffer, which is at least as wide as either operand's words; the words above it are
    // cleared
    public static void Copy(GpuBigUInt chosen, GpuBigUInt resultBuffer)
    {
        var words = chosen.NumWords;
        resultBuffer.CopyFrom(chosen, numWords: words);
        ResultBuffers.ClearAbove(resultBuffer, words);
    }

    public static InvalidOperationException NotOnTheHost() =>
        new("Max and Min have no host computation: a host plan names the chosen operand, which is copied.");
}
