using Kassyi.GSharp.Core;

// Records written by hand in place of the generator (GSharpExportDeviceCode=false): Twice calls the internal Helper,
// which the library's build could not transpile for C++. PRELUDE_HASH is replaced by the test
[assembly: GpuDeviceExport(1, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Twice(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Twice",
    CudaReferences = new[] { "__gsharp_fn_XasmLib__LibMath__Helper" },
    CppReferences = new[] { "__gsharp_fn_XasmLib__LibMath__Helper" },
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x){return __gsharp_fn_XasmLib__LibMath__Helper(x)*2;}",
    CppSignature = "int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CppBody = "int __gsharp_fn_XasmLib__LibMath__Twice(int x){return __gsharp_fn_XasmLib__LibMath__Helper(x)*2;}",
    Effect = "None")]
[assembly: GpuDeviceExport(1, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Helper(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Helper",
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Helper(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Helper(int x){return x;}",
    CppFailed = true, Effect = "None")]
