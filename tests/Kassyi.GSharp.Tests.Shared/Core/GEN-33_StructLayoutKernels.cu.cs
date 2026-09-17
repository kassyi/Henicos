using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

#if !NET8_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class InlineArrayAttribute : Attribute
    {
        public InlineArrayAttribute(int length) { Length = length; }
        public int Length { get; }
    }
}
#endif

namespace Kassyi.GSharp.Tests.Core
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CustomLayoutStruct
    {
        public byte A;
        public double B;
        public float C;
    }

    [InlineArray(4)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PackedInlineArray
    {
        public byte Element;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StructWithPackedInlineArray
    {
        public byte A;
        public PackedInlineArray Buffer;
        public int B;
    }

    public class GEN_33_StructLayoutKernels
    {
        [GpuKernel]
        public static void ModifyStructLayoutKernel(CustomLayoutStruct[] data)
        {
            int tid = ThreadIndex.X;

            data[tid].A += 10;
            data[tid].B *= 2.0;
            data[tid].C -= 1.0f;
        }

        [GpuKernel]
        public static void ModifyPackedInlineArrayKernel(StructWithPackedInlineArray[] data)
        {
            int tid = ThreadIndex.X;

            data[tid].A += 1;
            data[tid].Buffer.Element += 2;
            data[tid].B += 3;
        }
    }
}
