using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace IrBodyReasons;

public static class IrBodyReasonCases
{
    public static void Eligible(bool flag) { bool b = flag; return; }
    public static void OpaqueForeach(int[] a) { foreach (var v in a) { } }
    public static void OpaqueLambda(bool flag) { System.Func<int> f = () => 1; }
    public static void RemovedCallStatement(bool flag) { Trace(1); }
    public static void MacroForUnroll(bool flag) { Gpu.ForUnroll(0, 4, i => { }); }
    public static void MacroNotNull(int[] a) { Gpu.NotNull(a); }
    public static void SharedMemoryLocalUnused(bool flag) { var m = new SharedMemory<int>(8); }
    public static void UnsupportedAssignment(int[] a) { a[0] = 1; }
    public static void UnsupportedCall(bool flag) { Helper(1); }
    public static void UnsupportedSequence(bool flag) { int x = 0; int y = Bump(ref x) + x; }
    public static void BarrierInBody(bool flag) { Gpu.SyncThreads(); return; }
    public static void BarrierThenForeach(int[] a) { Gpu.SyncThreads(); foreach (var v in a) { } }
    public static void SplitInsideRemovedCall(int v) { Trace(Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1)); }
    public static void DefaultConstant(bool flag) { int x = default; var z = default(int); int s = sizeof(int); }

    // Nodes without a row of the C++ writer that hold a decision: a conversion, a property and a call
    public static void DecidedShapes(int[] a, int x) { long l = x; int n = a.Length; Helper(1); }

    public static void Generic<T>(T t) { }

    [Conditional("NEVER")]
    public static void Trace(int v) { }

    public static void Helper(int v) { }

    public static int Bump(ref int v) { v += 1; return v; }
}
