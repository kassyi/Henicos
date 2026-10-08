namespace Kassyi.GSharp.Core;

/// <summary>
/// Indicates that a method imports an external CUDA kernel or device function defined in a separate CUDA module.
/// </summary>
/// <remarks>
/// <para><b>Usage:</b> Applied to C# partial or extern method declarations that are implemented in external CUDA source files.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CudaImportAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the CUDA module containing the external kernel implementation.
    /// </summary>
    public string ModuleName
    {
        get;
    }

    /// <summary>
    /// Gets or sets the entry point name within the CUDA module. Defaults to the C# method name if empty.
    /// </summary>
    public string EntryPoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether kernel arguments should be packed into a context structure.
    /// </summary>
    public bool PackContext
    {
        get; set;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CudaImportAttribute"/> class with the specified module name.
    /// </summary>
    /// <param name="moduleName">The name or path of the external CUDA module file.</param>
    public CudaImportAttribute(string moduleName)
    {
        ModuleName = moduleName;
    }
}

