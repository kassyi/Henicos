namespace Kassyi.GSharp.Core;

using System.Diagnostics.CodeAnalysis;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[AttributeUsage(AttributeTargets.Method)]
public class GpuKernelAttribute : Attribute
{
    public bool Cooperative
    {
        get; set;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class GpuDeviceAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuInlineAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuNoInlineAttribute : Attribute;
