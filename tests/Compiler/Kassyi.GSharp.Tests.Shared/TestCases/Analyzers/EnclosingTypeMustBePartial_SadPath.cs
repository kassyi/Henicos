using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public class OuterNonPartialClass
{
    public struct OuterNonPartialStruct
    {
        public partial class InnerKernelClass
        {
            [GpuKernel]
            public static void Kernel()
            {
            }
        }
    }
}

public record OuterNonPartialRecord
{
    public partial class InnerDeviceClass
    {
        [GpuDeviceFunction]
        public static int DeviceFunc(int x) => x + 1;
    }
}

// A struct that holds device functions is reopened like a class (its managed version is written there): GSH0036
public struct DeviceOnlyStruct
{
    [GpuDeviceFunction]
    public static int Twice(int x) => x * 2;
}

// Reported once, although it encloses three GPU methods in two classes
public class OuterOfTwoClasses
{
    public partial class First
    {
        [GpuKernel]
        public static void KernelA()
        {
        }

        [GpuKernel]
        public static void KernelB()
        {
        }
    }

    public partial class Second
    {
        [GpuDeviceFunction]
        public static int Inc(int x) => x + 1;
    }
}

// A record class and a record struct are reopened like a class
public record NonPartialKernelRecord
{
    [GpuKernel]
    public static void Kernel()
    {
    }
}

public record struct DeviceOnlyRecordStruct
{
    [GpuDeviceFunction]
    public static int Twice(int x) => x * 2;
}

// Partial structs that hold device functions are not reported
public partial struct PartialDeviceStruct
{
    [GpuDeviceFunction]
    public static int Twice(int x) => x * 2;
}

public partial record struct PartialDeviceRecordStruct
{
    [GpuDeviceFunction]
    public static int Twice(int x) => x * 2;
}
