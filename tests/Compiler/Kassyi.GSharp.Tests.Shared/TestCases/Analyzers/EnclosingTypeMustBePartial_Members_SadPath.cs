using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

// An operator, an accessor and a member of an extension block that are device functions are methods of their type too
public class NonPartialOperatorClass
{
    [GpuDeviceFunction]
    public static NonPartialOperatorClass operator +(NonPartialOperatorClass a, NonPartialOperatorClass b) => a;
}

public class NonPartialAccessorClass
{
    public int P
    {
        [GpuDeviceFunction]
        get => 1;
    }
}

// The extension block cannot be partial: the class around it is the one that is reported
public static class NonPartialExtensions
{
    extension(int value)
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;
    }
}

// A generic struct, and a struct inside a partial class: only the struct is reported
public struct NonPartialGenericStruct<T>
{
    [GpuDeviceFunction]
    public static int Twice(int x) => x * 2;
}

public partial class PartialOuterOfStruct
{
    public struct NonPartialInnerStruct
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;
    }
}

// Not reported: every type around the device function is partial
public partial struct PartialGenericStruct<T>
{
    [GpuDeviceFunction]
    public static int Twice(int x) => x * 2;
}

public readonly partial struct PartialReadonlyStruct
{
    [GpuDeviceFunction]
    public static int Twice(int x) => x * 2;
}

public ref partial struct PartialRefStruct
{
    [GpuDeviceFunction]
    public static int Twice(int x) => x * 2;
}

public partial class PartialOuterOfPartialStruct
{
    public partial struct PartialInnerStruct
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;
    }
}

public partial class PartialOperatorClass
{
    [GpuDeviceFunction]
    public static PartialOperatorClass operator +(PartialOperatorClass a, PartialOperatorClass b) => a;
}

public static partial class PartialExtensions
{
    extension(int value)
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;
    }
}
