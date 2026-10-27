using Kassyi.GSharp.Core;

// Records written by hand (GSharpExportDeviceCode=false): Alpha's computed property calls Helper, which the library's
// build could not transpile for C++. PRELUDE_HASH is replaced by the test
[assembly: GpuDeviceExport(1, "PRELUDE_HASH", "Struct", "T:XasmLib.Alpha", CppName = "__gsharp_ty_XasmLib__Alpha",
    CudaReferences = new[] { "__gsharp_fn_XasmLib__LibMath__Helper" },
    CppReferences = new[] { "__gsharp_fn_XasmLib__LibMath__Helper" },
    CudaBody = "#ifndef __GSHARP_STRUCT___gsharp_ty_XasmLib__Alpha__\n#define __GSHARP_STRUCT___gsharp_ty_XasmLib__Alpha__\nstruct __gsharp_ty_XasmLib__Alpha\n{\nint __gsharp_mb_V;\n__device__ inline int __gsharp_mb_Probe() const { return __gsharp_fn_XasmLib__LibMath__Helper(__gsharp_mb_V); }\n};\n#endif\n",
    CppBody = "#ifndef __GSHARP_STRUCT___gsharp_ty_XasmLib__Alpha__\n#define __GSHARP_STRUCT___gsharp_ty_XasmLib__Alpha__\nstruct __gsharp_ty_XasmLib__Alpha\n{\nint __gsharp_mb_V;\n__device__ inline int __gsharp_mb_Probe() const { return __gsharp_fn_XasmLib__LibMath__Helper(__gsharp_mb_V); }\n};\n#endif\n")]
[assembly: GpuDeviceExport(1, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Helper(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Helper",
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Helper(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Helper(int x){return x;}",
    CppFailed = true, Effect = "None")]

namespace XasmLib
{
    public struct Alpha
    {
        public int V;
    }
}
