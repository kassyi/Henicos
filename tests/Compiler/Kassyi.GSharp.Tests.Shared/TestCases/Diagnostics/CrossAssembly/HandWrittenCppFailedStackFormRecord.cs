using Kassyi.GSharp.Core;

// A record written by hand where ManagedStackForm is true but CppFailed is true. FORMAT_VERSION and PRELUDE_HASH are
// replaced by the test
[assembly: GpuDeviceExport(FORMAT_VERSION, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Twice(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Twice",
    FrameName = "", Suspends = true, ReachesBlockBarrier = true,
    ManagedStackForm = true, CppFailed = true,
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x){__syncthreads();return x*2;}",
    Effect = "None")]
