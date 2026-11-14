using Kassyi.GSharp.Core;

// A record written by hand in place of the generator (GSharpExportDeviceCode=false): PRELUDE_HASH is replaced by the
// test and TRAITS by the named arguments that the test gives the record, so that it can leave a trait out as the
// records of an older G# do
[assembly: GpuDeviceExport(3, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Twice(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Twice",
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x){return x*2;}",
    CppSignature = "int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CppBody = "int __gsharp_fn_XasmLib__LibMath__Twice(int x){return x*2;}",
    Effect = "None"TRAITS)]
