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

/// <summary>Represents <c>cublasOperation_t</c>, the operation applied to a matrix operand.</summary>
public enum CuBlasOperation
{
    /// <summary>The matrix is used as-is (<c>CUBLAS_OP_N</c>).</summary>
    None = 0,

    /// <summary>The matrix is transposed (<c>CUBLAS_OP_T</c>).</summary>
    Transpose = 1,

    /// <summary>The matrix is conjugate-transposed (<c>CUBLAS_OP_C</c>).</summary>
    ConjugateTranspose = 2
}

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

/// <summary>Represents <c>curandRngType_t</c>, the algorithm used by a cuRAND generator.</summary>
public enum CuRandRngType
{
    PseudoDefault = 100,
    PseudoXorwow = 101,
    PseudoMrg32k3a = 121,
    PseudoMtgp32 = 141,
    PseudoMt19937 = 142,
    PseudoPhilox4x32x10 = 161,
    QuasiDefault = 200,
    QuasiSobol32 = 201,
    QuasiScrambledSobol32 = 202,
    QuasiSobol64 = 203,
    QuasiScrambledSobol64 = 204
}
