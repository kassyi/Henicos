#nullable enable
using Kassyi.GSharp.Core;

namespace IrCppCases;

public static partial class IrCppDeviceFunctions
{
    [GpuDeviceFunction] public static void Empty(bool flag) { }
    [GpuDeviceFunction] public static void Declarations(bool flag) { int x = 3; bool b = flag; int y = x; }
    [GpuDeviceFunction] public static int ReturnsConstant(bool flag) { return 7; }
    [GpuDeviceFunction] public static int ReturnsLocal(bool flag) { int x = 3; return x; }
    [GpuDeviceFunction] public static int ExpressionBody(int v) => v;
    [GpuDeviceFunction] public static void IfElse(bool flag) { if (flag) { int y = 1; } else { int z = 2; } }
    [GpuDeviceFunction] public static void IfWithoutBraces(bool flag) { if (flag) return; int y = 1; }
    [GpuDeviceFunction] public static void EarlyReturn(bool flag) { if (flag) { return; } int y = 1; }
    [GpuDeviceFunction] public static void WhileFlag(bool flag) { while (flag) { int y = 1; } }
    [GpuDeviceFunction] public static void NestedBlock(bool flag) { { int y = 1; } int z = 2; }
    [GpuDeviceFunction] public static void MultiDeclarator(bool flag) { int a = 1, b = a; int c = b; }

    [GpuDeviceFunction]
    public static void ConstantForms(bool flag)
    {
        char c = '_'; int n = 1_000; bool t = true; long l = 5L; float f = 1.5f;
    }

    [GpuDeviceFunction] public static void VarDeclaration(bool flag) { var x = 3; var b = flag; }
    [GpuDeviceFunction] public static void NullArray(bool flag) { int[]? a = null; }

    // A recursive device function that reaches a barrier (GSH0061) is a plain device function of the emission plan,
    // since a frame of no static size cannot be a state machine; its barrier keeps it off the IR route
    [GpuDeviceFunction]
    public static void RecursiveBarrier(int depth)
    {
        if (depth <= 0) return;
        Gpu.SyncThreads();
        RecursiveBarrier(depth - 1);
    }
}
