using System;
using Kassyi.GSharp.Core;

// A [BclIntrinsicMap] that the dialect maps as well, read by IrNullDecisionTests: the BCL map claims the call (#1561).
[assembly: BclIntrinsicMap("System.Math", "Abs", ["System.Single"], "user_fabsf")]

public static class Overlap
{
    public static void Call(float f, float[] r) { r[0] = Math.Abs(f); }
}
