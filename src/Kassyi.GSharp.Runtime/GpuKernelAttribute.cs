namespace Kassyi.GSharp.Runtime;

[AttributeUsage(AttributeTargets.Method)]
public class GpuKernelAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public class GpuDeviceAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuInlineAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuNoInlineAttribute : Attribute;
