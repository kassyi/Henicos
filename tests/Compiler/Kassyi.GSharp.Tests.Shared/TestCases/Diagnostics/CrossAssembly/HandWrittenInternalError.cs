using Kassyi.GSharp.Core;

// A record written by hand (GSharpExportDeviceCode=false) whose C++ text failed with an exception of the generator.
// PRELUDE_HASH is replaced by the test
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Twice(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Twice",
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x){return x*2;}",
    CppFailed = true, InternalError = "InvalidOperationException: boom", Effect = "None")]
