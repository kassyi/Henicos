using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics
{
    // GSH0003 for each reference to dynamic, TypedReference and GCHandle, decided by the symbol
    public static partial class RuntimeOnlyTypeKernels
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void DynamicKernel(int[] output)
        {
            dynamic value = 42;
            output[0] = 1;
        }

        // __makeref is an unsupported syntax kind of the transpiler
        [GpuKernel]
        public static void TypedReferenceKernel(int[] output)
        {
            int value = 10;
            TypedReference reference = __makeref(value);
            output[0] = value;
        }

        // The type, the receiver of Alloc and the var that stands for GCHandle
        [GpuKernel]
        public static void GCHandleKernel(int[] output)
        {
            GCHandle handle = GCHandle.Alloc(output);
            var copy = handle;
            output[0] = 1;
        }

        // Not reported: a user struct with the same name
        [GpuKernel]
        public static void UserGCHandleKernel(int[] output)
        {
            UserTypes.GCHandle handle = default;
            output[0] = handle.Value;
        }

        public const decimal Rate = 1.5m;

        // decimal: the keyword, the literal, the var that stands for it and each reference to a field, local or parameter
        [GpuKernel]
        public static void DecimalKernel(double[] output)
        {
            output[0] = (double)Rate;
            decimal local = 2.5m;
            var copy = local;
            output[1] = (double)copy + Halve(3.5m);
        }

        [GpuDeviceFunction]
        public static double Halve(decimal value) => (double)value / 2;

        // decimal through a property and through the return value of a call
        [GpuKernel]
        public static void DecimalMemberKernel(double[] output)
        {
            output[0] = (double)RateProperty + (double)ToDecimal(output[0]);
        }

        public static decimal RateProperty => 1.5m;

        [GpuDeviceFunction]
        public static decimal ToDecimal(double value) => (decimal)value;
    }
}

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics.UserTypes
{
    public struct GCHandle
    {
        public int Value;
    }
}
