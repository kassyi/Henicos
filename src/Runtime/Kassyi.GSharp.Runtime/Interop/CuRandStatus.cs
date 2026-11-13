namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents <c>curandStatus_t</c> return codes of the cuRAND library.</summary>
public enum CuRandStatus
{
    Success = 0,
    VersionMismatch = 100,
    NotInitialized = 101,
    AllocationFailed = 102,
    TypeError = 103,
    OutOfRange = 104,
    LengthNotMultiple = 105,
    DoublePrecisionRequired = 106,
    LaunchFailure = 201,
    PreexistingFailure = 202,
    InitializationFailed = 203,
    ArchMismatch = 204,
    InternalError = 999
}
