using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler;

[SkipSnapshotConsistency]
public partial class GpuMacroTranspilerRule_SadPath
{
    [GpuKernel]
    public static void Kernel()
    {
        var ptx = "nop;";
        // This will trigger GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral inside GpuMacroTranspilerRule
        Gpu.InlinePtx(ptx); 

        // This will trigger TranspileConstant path inside GpuMacroTranspilerRule
        var c = Gpu.Constant("MyConst");
    }
}
