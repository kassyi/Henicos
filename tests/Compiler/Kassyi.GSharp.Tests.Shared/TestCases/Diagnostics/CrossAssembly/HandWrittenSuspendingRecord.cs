using Kassyi.GSharp.Core;

// A record written by hand (GSharpExportDeviceCode=false) of a function that suspends, by a library that holds no
// managed state machine for it. PRELUDE_HASH is replaced by the test
[assembly: GpuDeviceExport(1, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Twice(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Twice",
    FrameName = "__gsharp_frame_XasmLib__LibMath__Twice", Suspends = true, ReachesBlockBarrier = true,
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x){__syncthreads();return x*2;}",
    CppSignature = "struct __gsharp_frame_XasmLib__LibMath__Twice;",
    CppBody = "struct __gsharp_frame_XasmLib__LibMath__Twice {};",
    Effect = "None")]
