namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents function attributes that can be queried from a CUfunction handle.</summary>
public enum CuFunctionAttribute
{
    MaxThreadsPerBlock = 0,
    SharedSizeBytes = 1,
    ConstSizeBytes = 2,
    LocalSizeBytes = 3,
    NumRegs = 4,
    PtxVersion = 5,
    BinaryVersion = 6,

    /// <summary>
    /// The largest dynamic shared memory in bytes that a launch of the function may request. It starts at the default
    /// per-block limit minus the static shared memory and can be raised with <c>cuFuncSetAttribute</c> up to the opt-in
    /// limit of the device minus the static shared memory.
    /// </summary>
    MaxDynamicSharedSizeBytes = 8
}
