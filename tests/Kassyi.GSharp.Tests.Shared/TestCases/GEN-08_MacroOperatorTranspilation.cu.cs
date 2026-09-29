// ReSharper disable All
using Kassyi.GSharp.Core;
using System;

namespace TestNamespace
{
    public static partial class DummyKernels
    {
        [GpuDeviceFunction]
        public static void DummyOp(ref DummyContext ctx, uint[] result, uint[] left, uint[] right) { }

        [GpuDeviceFunction]
        public static void DummyUnaryOp(uint[] result, uint[] operand) { }

        [GpuDeviceFunction]
        public static void DummyShiftOp(DummyContext ctx, uint[] result, uint[] operand, int shift) { }
    }

    public struct DummyContext
    {
        public int State;
    }

    public readonly ref struct DummyRef
    {
        public readonly uint[] Data;

        public DummyRef(uint[] data) => Data = data;

        [GpuOperatorMacro(typeof(DummyKernels), nameof(DummyKernels.DummyOp))]
        public static DummyRef operator +(DummyRef a, DummyRef b) => throw new Exception();

        [GpuOperatorMacro(typeof(DummyKernels), nameof(DummyKernels.DummyUnaryOp))]
        public static DummyRef operator ~(DummyRef a) => throw new Exception();

        [GpuOperatorMacro(typeof(DummyKernels), nameof(DummyKernels.DummyShiftOp))]
        public static DummyRef operator <<(DummyRef a, int shift) => throw new Exception();

        public static implicit operator DummyRef(uint[] data) => new DummyRef(data);
        public static implicit operator uint[](DummyRef data) => data.Data;
    }

    public static partial class TestKernels
    {
        [GpuKernel]
        public static void MacroKernel(DummyContext ctx, uint[] dA, uint[] dB, uint[] dResult)
        {
            DummyRef a = dA;
            DummyRef b = dB;
            DummyRef c = dResult;
            
            c = a + b;
            c = ~a;
            c = a << 5;
        }
    }
}
