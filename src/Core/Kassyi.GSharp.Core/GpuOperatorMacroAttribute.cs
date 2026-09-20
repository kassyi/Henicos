namespace Kassyi.GSharp.Core;

/// <summary>
/// Specifies that a C# custom operator overload maps to a specific GPU device function.
/// The transpiler will dispatch the operator to this method without hardcoding its details.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GpuOperatorMacroAttribute : Attribute
{
    /// <summary>
    /// The target type containing the GPU kernel or device method.
    /// </summary>
    public Type TargetType
    {
        get;
    }

    /// <summary>
    /// The name of the target GPU kernel or device method.
    /// </summary>
    public string TargetMethod
    {
        get;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuOperatorMacroAttribute"/> class.
    /// </summary>
    /// <param name="targetType">The target type containing the GPU kernel or device method.</param>
    /// <param name="targetMethod">The name of the target GPU kernel or device method.</param>
    public GpuOperatorMacroAttribute(Type targetType, string targetMethod)
    {
        TargetType = targetType;
        TargetMethod = targetMethod;
    }
}
