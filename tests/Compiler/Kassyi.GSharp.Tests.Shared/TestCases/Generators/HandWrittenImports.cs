using Kassyi.GSharp.Core;

// Records written by hand (GSharpExportDeviceCode=false), whose texts are markers: PRELUDE_HASH is replaced by the
// test. F reaches G (which failed for CUDA), the struct S (only on CUDA) and Dup; Alpha names Zeta only through its
// constructor; Relay's frame embeds Wait's; Box<int> is a struct that the consumer declares itself
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Function", "M:H.Lib.F", CppName = "__gsharp_fn_H__Lib__F",
    CudaReferences = new[] { "__gsharp_fn_H__Lib__G", "__gsharp_ty_H__S", "__gsharp_fn_H__Lib__Dup" },
    CppReferences = new[] { "__gsharp_fn_H__Lib__G", "__gsharp_fn_H__Lib__Dup" },
    CudaSignature = "/*F_CUDA_SIGNATURE*/", CudaBody = "/*F_CUDA_BODY*/",
    CppSignature = "/*F_CPP_SIGNATURE*/", CppBody = "/*F_CPP_BODY*/",
    CudaIncludes = new[] { "<f_cuda.h>" }, CppIncludes = new[] { "<f_cpp.h>" })]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Function", "M:H.Lib.G", CppName = "__gsharp_fn_H__Lib__G",
    CudaSignature = "/*G_CUDA_SIGNATURE*/", CudaBody = "/*G_CUDA_BODY*/",
    CppSignature = "/*G_CPP_SIGNATURE*/", CppBody = "/*G_CPP_BODY*/", CudaFailed = true)]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Function", "M:H.Lib.Dup", CppName = "__gsharp_fn_H__Lib__Dup",
    CudaSignature = "/*DUP_SIGNATURE*/", CudaBody = "/*DUP_BODY_ASSEMBLY_NAME*/",
    CppSignature = "/*DUP_SIGNATURE*/", CppBody = "/*DUP_BODY_ASSEMBLY_NAME*/")]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Struct", "T:H.S", CppName = "__gsharp_ty_H__S",
    CppFullName = "__gsharp_ty_H__S_full",
    DefinedNames = new[] { "__gsharp_ctor_H__S_ab12" }, DefinedIds = new[] { "M:H.S.#ctor(System.Int32)" },
    DefinedFullNames = new[] { "__gsharp_ctor_H__S_full" },
    CudaBody = "/*S_CUDA_BODY*/", CppBody = "/*S_CPP_BODY*/", CudaIncludes = new[] { "<s_cuda.h>" })]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Struct", "T:H.Alpha", CppName = "__gsharp_ty_H__Alpha",
    CudaReferences = new[] { "__gsharp_ctor_H__Zeta" }, CppReferences = new[] { "__gsharp_ctor_H__Zeta" },
    CudaBody = "/*ALPHA_BODY*/", CppBody = "/*ALPHA_BODY*/")]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Struct", "T:H.Zeta", CppName = "__gsharp_ty_H__Zeta",
    DefinedNames = new[] { "__gsharp_ctor_H__Zeta" }, DefinedIds = new[] { "M:H.Zeta.#ctor(System.Int32)" },
    CudaBody = "/*ZETA_BODY*/", CppBody = "/*ZETA_BODY*/")]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Function", "M:H.Lib.Relay", CppName = "__gsharp_fn_H__Lib__Relay",
    CudaReferences = new[] { "__gsharp_fn_H__Lib__Wait" }, CppReferences = new[] { "__gsharp_fn_H__Lib__Wait" },
    FrameName = "__gsharp_frame_H__Lib__Relay", FrameDependencies = new[] { "__gsharp_frame_H__Lib__Wait" },
    Suspends = true, CudaSignature = "/*RELAY_SIGNATURE*/", CudaBody = "/*RELAY_BODY*/",
    CppSignature = "/*RELAY_SIGNATURE*/", CppBody = "/*RELAY_BODY*/")]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Function", "M:H.Lib.Wait", CppName = "__gsharp_fn_H__Lib__Wait",
    FrameName = "__gsharp_frame_H__Lib__Wait", Suspends = true, CudaSignature = "/*WAIT_SIGNATURE*/",
    CudaBody = "/*WAIT_BODY*/", CppSignature = "/*WAIT_SIGNATURE*/", CppBody = "/*WAIT_BODY*/")]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Struct", "T:H.Lost", CppName = "__gsharp_ty_H__Lost",
    CudaReferences = new[] { "__gsharp_fn_H__Lib__Missing" }, CppReferences = new[] { "__gsharp_fn_H__Lib__Missing" },
    CudaBody = "/*LOST_BODY*/", CppBody = "/*LOST_BODY*/")]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Struct", "T:H.Twin", CppName = "__gsharp_ty_H__Twin",
    CudaReferences = new[] { "__gsharp_fn_H__Lib__Dup" }, CppReferences = new[] { "__gsharp_fn_H__Lib__Dup" },
    CudaBody = "/*TWIN_BODY*/", CppBody = "/*TWIN_BODY*/")]
[assembly: GpuDeviceExport(2, "PRELUDE_HASH", "Struct", "T:H.Box{System.Int32}", CppName = "__gsharp_ty_H__Box_g1_bint_e",
    CudaBody = "/*BOX_BODY*/", CppBody = "/*BOX_BODY*/")]
