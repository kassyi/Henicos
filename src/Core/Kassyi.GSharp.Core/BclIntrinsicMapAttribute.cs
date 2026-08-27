namespace Kassyi.GSharp.Core;

/// <summary>
/// Defines a mapping from a BCL (Base Class Library) method to a CUDA/PTX intrinsic function.
/// This attribute is used by the GSharp Compiler to translate C# standard library calls into GPU-native instructions without relying on reflection or hardcoded transpile rules.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class BclIntrinsicMapAttribute : Attribute
{
    /// <summary>
    /// The fully qualified name of the BCL type containing the method (e.g., "System.Numerics.BitOperations").
    /// </summary>
    public string BclTypeFullName { get; }

    /// <summary>
    /// The name of the method.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// The fully qualified names of the parameter types to match the exact overload (e.g., ["System.UInt32"]).
    /// </summary>
    public IReadOnlyList<string> ParameterTypeFullNames { get; }

    /// <summary>
    /// If specified, indicates that the first argument should be cast to this C++ type (e.g. "unsigned int") before calling the intrinsic.
    /// </summary>
    public string? CastFirstArgumentTo { get; set; }

    /// <summary>
    /// The PTX or CUDA C++ intrinsic function name to emit (e.g., "__popc").
    /// </summary>
    public string CudaIntrinsicName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BclIntrinsicMapAttribute"/> class.
    /// </summary>
    /// <param name="bclTypeFullName">The fully qualified name of the BCL type.</param>
    /// <param name="methodName">The name of the method.</param>
    /// <param name="parameterTypeFullNames">The fully qualified names of the method parameters.</param>
    /// <param name="cudaIntrinsicName">The PTX or CUDA C++ intrinsic function name.</param>
    public BclIntrinsicMapAttribute(string bclTypeFullName, string methodName, string[] parameterTypeFullNames, string cudaIntrinsicName)
    {
        BclTypeFullName = bclTypeFullName;
        MethodName = methodName;
        ParameterTypeFullNames = Array.AsReadOnly(parameterTypeFullNames);
        CudaIntrinsicName = cudaIntrinsicName;
    }
}
