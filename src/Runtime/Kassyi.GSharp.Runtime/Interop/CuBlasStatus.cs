namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents <c>cublasStatus_t</c> return codes of the cuBLAS library.</summary>
public enum CuBlasStatus
{
    Success = 0,
    NotInitialized = 1,
    AllocFailed = 3,
    InvalidValue = 7,
    ArchMismatch = 8,
    MappingError = 11,
    ExecutionFailed = 13,
    InternalError = 14,
    NotSupported = 15,
    LicenseError = 16
}
