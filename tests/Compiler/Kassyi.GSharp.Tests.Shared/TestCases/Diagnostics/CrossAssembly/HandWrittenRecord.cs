using Kassyi.GSharp.Core;

// Records written by hand in place of the generator (GSharpExportDeviceCode=false): PRELUDE_HASH, CUDA_FAILED and
// CPP_FAILED are replaced by the test
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Twice(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Twice",
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x){return x*2;}",
    CppSignature = "int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CppBody = "int __gsharp_fn_XasmLib__LibMath__Twice(int x){return x*2;}",
    CudaFailed = CUDA_FAILED, CppFailed = CPP_FAILED, Effect = "None")]
