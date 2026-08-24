#pragma warning disable IDE0051, IDE0044, CS0169
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Stdlib.Math.Contexts;

public struct NttMulContext
{
    public OperandsBuffers Operands;
    public NttBuffers Ntt;
    public NttTwiddleFactors TwiddleFactors;
    public NttMulDimensions Dimensions;

    public struct OperandsBuffers
    {
        [GpuConst] public uint[] A;
        [GpuConst] public uint[] B;
        public uint[] Result;
    }

    public struct NttBuffers
    {
        public uint[] Buffer1A;
        public uint[] Buffer2A;
        public uint[] Buffer1B;
        public uint[] Buffer2B;
        public uint[] TemporaryBuffer;
    }

    public struct NttTwiddleFactors
    {
        [GpuConst] public uint[] Forward1;
        [GpuConst] public uint[] Forward2;
        [GpuConst] public uint[] Inverse1;
        [GpuConst] public uint[] Inverse2;
    }

    public struct NttMulDimensions
    {
        public int NumWordsA;
        public int NumWordsB;
        public int OutNumWords;
        public int TransformLength;
    }
}

