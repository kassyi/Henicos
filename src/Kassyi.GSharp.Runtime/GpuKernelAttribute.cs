namespace Kassyi.GSharp.Runtime;

[AttributeUsage(AttributeTargets.Method)]
public class GpuKernelAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public class GpuDeviceAttribute : Attribute;
