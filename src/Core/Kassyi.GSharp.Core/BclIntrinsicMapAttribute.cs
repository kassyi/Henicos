namespace Kassyi.GSharp.Core;

/// <summary>
/// Maps a Base Class Library (BCL) method to a CUDA C++ or PTX intrinsic instruction during source generation.
/// </summary>
/// <remarks>
/// <para>This assembly-level attribute enables the GSharp Transpiler to translate standard .NET API calls (e.g., <c>BitOperations.PopCount</c>) into optimal GPU hardware instructions without runtime reflection.</para>
/// <para><b>Target:</b> Assembly declaration.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class BclIntrinsicMapAttribute : Attribute
{
    /// <summary>
    /// Gets the fully qualified name of the target BCL type (e.g., <c>System.Numerics.BitOperations</c>).
    /// </summary>
    public string BclTypeFullName { get; }

    /// <summary>
    /// Gets the name of the method within the target BCL type.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// Gets the list of fully qualified parameter type names used to resolve the specific method overload.
    /// </summary>
    public IReadOnlyList<string> ParameterTypeFullNames { get; }

    /// <summary>
    /// Gets or sets an optional C++ type cast applied to the first argument before calling the intrinsic (e.g., <c>unsigned int</c>).
    /// </summary>
    public string? CastFirstArgumentTo { get; set; }

    /// <summary>
    /// Gets the CUDA C++ or PTX intrinsic function name to emit (e.g., <c>__popc</c>).
    /// </summary>
    public string CudaIntrinsicName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BclIntrinsicMapAttribute"/> class.
    /// </summary>
    /// <param name="bclTypeFullName">The fully qualified name of the BCL type containing the method.</param>
    /// <param name="methodName">The name of the method to map.</param>
    /// <param name="parameterTypeFullNames">The fully qualified parameter type names for overload matching.</param>
    /// <param name="cudaIntrinsicName">The CUDA C++ or PTX intrinsic function name.</param>
    public BclIntrinsicMapAttribute(string bclTypeFullName, string methodName, string[] parameterTypeFullNames,
        string cudaIntrinsicName)
    {
        BclTypeFullName = bclTypeFullName;
        MethodName = methodName;
        ParameterTypeFullNames = Array.AsReadOnly(parameterTypeFullNames);
        CudaIntrinsicName = cudaIntrinsicName;
    }
}
