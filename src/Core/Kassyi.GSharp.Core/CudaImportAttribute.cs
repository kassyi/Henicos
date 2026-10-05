namespace Kassyi.GSharp.Core;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CudaImportAttribute : Attribute
{
    public string ModuleName { get; }
    public string EntryPoint { get; set; } = string.Empty;
    public bool PackContext { get; set; }

    public CudaImportAttribute(string moduleName)
    {
        ModuleName = moduleName;
    }
}
