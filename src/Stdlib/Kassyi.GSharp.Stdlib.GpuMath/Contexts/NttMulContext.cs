#pragma warning disable IDE0051, IDE0044, CS0169

namespace Kassyi.GSharp.Stdlib.GpuMath.Contexts;

/// <summary>Encapsulates parameter buffers, dimensions, and twiddle factor pointers for NTT-based big integer multiplication kernels.</summary>
public struct NttMulContext
{
    /// <summary>The operand buffers containing input values and output result memory.</summary>
    public OperandsBuffers Operands;
    /// <summary>The temporary scratch buffers allocated for intermediate NTT passes.</summary>
    public NttBuffers Ntt;
    /// <summary>The precomputed twiddle factors table for forward and inverse NTT.</summary>
    public NttTwiddleFactors TwiddleFactors;
    /// <summary>The dimension metadata describing word counts and transform length.</summary>
    public NttMulDimensions Dimensions;

    /// <summary>Contains device pointers for input operands and destination buffer.</summary>
    public struct OperandsBuffers
    {
        /// <summary>Input operand A.</summary>
        [GpuReadOnly] public uint[] A;
        /// <summary>Input operand B.</summary>
        [GpuReadOnly] public uint[] B;
        /// <summary>Destination output result buffer.</summary>
        public uint[] Result;
    }

    /// <summary>Contains device scratch buffers used during forward/inverse NTT convolutions.</summary>
    public struct NttBuffers
    {
        /// <summary>First transform pass buffer for operand A.</summary>
        public uint[] Buffer1A;
        /// <summary>Second transform pass buffer for operand A.</summary>
        public uint[] Buffer2A;
        /// <summary>First transform pass buffer for operand B.</summary>
        public uint[] Buffer1B;
        /// <summary>Second transform pass buffer for operand B.</summary>
        public uint[] Buffer2B;
        /// <summary>Intermediate scratch buffer.</summary>
        public uint[] TemporaryBuffer;
    }

    /// <summary>Contains precomputed roots of unity and twiddle factor tables.</summary>
    public struct NttTwiddleFactors
    {
        /// <summary>Forward NTT twiddle factors for first prime modulus.</summary>
        [GpuReadOnly] public uint[] Forward1;
        /// <summary>Forward NTT twiddle factors for second prime modulus.</summary>
        [GpuReadOnly] public uint[] Forward2;
        /// <summary>Inverse NTT twiddle factors for first prime modulus.</summary>
        [GpuReadOnly] public uint[] Inverse1;
        /// <summary>Inverse NTT twiddle factors for second prime modulus.</summary>
        [GpuReadOnly] public uint[] Inverse2;
    }

    /// <summary>Specifies the word dimensions and transformation length for the multiplication.</summary>
    public struct NttMulDimensions
    {
        /// <summary>The number of 32-bit words in operand A.</summary>
        public int NumWordsA;
        /// <summary>The number of 32-bit words in operand B.</summary>
        public int NumWordsB;
        /// <summary>The number of 32-bit words in the output result.</summary>
        public int OutNumWords;
        /// <summary>The total power-of-two transformation length for the NTT convolution.</summary>
        public int TransformLength;
    }
}

