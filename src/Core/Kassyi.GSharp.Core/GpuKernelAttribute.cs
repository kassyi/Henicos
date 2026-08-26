namespace Kassyi.GSharp.Core;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[AttributeUsage(AttributeTargets.Method)]
public class GpuKernelAttribute : Attribute
{
    public bool Cooperative { get; set; }
}

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[AttributeUsage(AttributeTargets.Method)]
public class GpuDeviceFunctionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuInlineAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuNoInlineAttribute : Attribute;
