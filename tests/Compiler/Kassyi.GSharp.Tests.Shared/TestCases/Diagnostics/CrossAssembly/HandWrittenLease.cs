using System;
using Kassyi.GSharp.Core;

// A struct with a Dispose, whose record written by hand failed for C++ (GSharpExportDeviceCode=false). PRELUDE_HASH is
// replaced by the test
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Struct", "T:XasmLib.Lease", CppName = "__gsharp_ty_XasmLib__Lease",
    CudaBody = "#ifndef __GSHARP_STRUCT___gsharp_ty_XasmLib__Lease__\n#define __GSHARP_STRUCT___gsharp_ty_XasmLib__Lease__\nstruct __gsharp_ty_XasmLib__Lease\n{\nint __gsharp_mb_V;\n__device__ inline void __gsharp_dispose();\n};\n#endif\n",
    CudaDispose = "__device__ inline void __gsharp_ty_XasmLib__Lease::__gsharp_dispose(){__gsharp_mb_V=0;}\n",
    CppFailed = true)]

namespace XasmLib
{
    public struct Lease : IDisposable
    {
        public int V;

        public void Dispose()
        {
            V = 0;
        }
    }
}
