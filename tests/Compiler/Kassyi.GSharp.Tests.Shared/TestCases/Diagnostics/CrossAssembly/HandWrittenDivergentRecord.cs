using Kassyi.GSharp.Core;

// A record written by hand (GSharpExportDeviceCode=false) of a function whose barrier diverges when its first parameter
// varies within a block (DivergentConditionParameters bit 0). PRELUDE_HASH is replaced by the test
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Twice(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Twice",
    FrameName = "__gsharp_frame_XasmLib__LibMath__Twice", Suspends = true, ReachesBlockBarrier = true,
    DivergentConditionParameters = 1,
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x){if(x%2==0){__syncthreads();}__syncthreads();return x*2;}",
    CppSignature = "struct __gsharp_frame_XasmLib__LibMath__Twice;",
    CppBody = "struct __gsharp_frame_XasmLib__LibMath__Twice {};",
    Effect = "None")]
