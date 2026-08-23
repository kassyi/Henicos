namespace Kassyi.GSharp.Core;

// ReSharper disable UnusedType.Global
[AttributeUsage(AttributeTargets.Method)]
public class GpuKernelAttribute : Attribute
{
    public bool Cooperative { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public class GpuDeviceAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuInlineAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuNoInlineAttribute : Attribute;
