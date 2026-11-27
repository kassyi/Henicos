using Kassyi.GSharp.Core;

// A record written by hand of a suspending device function that has a managed stack form only and no state-machine
// form (StackOnly = true). FORMAT_VERSION and PRELUDE_HASH are replaced by the test
[assembly: GpuDeviceExport(FORMAT_VERSION, "PRELUDE_HASH", "Function", "M:XasmLib.LibMath.Twice(System.Int32)",
    CppName = "__gsharp_fn_XasmLib__LibMath__Twice",
    FrameName = "", Suspends = true, ReachesBlockBarrier = true,
    ManagedStackForm = true, StackOnly = true,
    CudaSignature = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CudaBody = "__device__ int __gsharp_fn_XasmLib__LibMath__Twice(int x){__syncthreads();return x*2;}",
    CppSignature = "inline int __gsharp_fn_XasmLib__LibMath__Twice(int x);",
    CppBody = "inline int __gsharp_fn_XasmLib__LibMath__Twice(int x){__gsharp_stack_barrier();return x*2;}",
    Effect = "None")]
