using Kassyi.GSharp.Core;

// Another assembly that records a function of the same C++ name as HandWrittenImports.cs's Dup
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Function", "M:H.Lib.Dup", CppName = "__gsharp_fn_H__Lib__Dup",
    CudaSignature = "/*DUP_SIGNATURE*/", CudaBody = "/*DUP_BODY_ASSEMBLY_NAME*/",
    CppSignature = "/*DUP_SIGNATURE*/", CppBody = "/*DUP_BODY_ASSEMBLY_NAME*/")]
