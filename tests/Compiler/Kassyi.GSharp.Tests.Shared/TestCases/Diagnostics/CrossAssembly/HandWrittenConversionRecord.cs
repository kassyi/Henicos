using Kassyi.GSharp.Core;

// The record of a struct with conversion operators, written by hand in place of the generator
// (GSharpExportDeviceCode=false): it failed for C++ and has its CUDA text. PRELUDE_HASH is replaced by the test
[assembly: GpuDeviceExport(FORMAT_VERSION, "PRELUDE_HASH", "Struct", "T:XasmLib.Meters",
    CppName = "__gsharp_ty_XasmLib__Meters",
    DefinedNames = new[] { "__gsharp_op_XasmLib__Meters__implicit_int", "__gsharp_op_XasmLib__Meters__implicit_double" },
    DefinedIds = new[] { "M:XasmLib.Meters.op_Implicit(System.Int32)~XasmLib.Meters", "M:XasmLib.Meters.op_Implicit(XasmLib.Meters)~System.Double" },
    CudaBody = "#ifndef __GSHARP_STRUCT___gsharp_ty_XasmLib__Meters__\n#define __GSHARP_STRUCT___gsharp_ty_XasmLib__Meters__\nstruct __gsharp_ty_XasmLib__Meters\n{\ndouble __gsharp_mb_V;\n};\n#endif\n",
    CppFailed = true)]

namespace XasmLib
{
    public struct Meters
    {
        public double V;

        public static implicit operator Meters(int i) => new Meters { V = i };
        public static implicit operator double(Meters m) => m.V;
    }
}
