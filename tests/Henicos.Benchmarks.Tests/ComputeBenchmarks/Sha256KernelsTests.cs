using System;
using System.Text;
using Xunit;
using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks.Tests.ComputeBenchmarks;

public class Sha256KernelsTests
{
    [Fact]
    public void ComputeBlock_MatchesStandardSha256()
    {
        // Arrange
        // For a single block, we can just hash an empty message with standard SHA-256 padding.
        // Or we can just provide an arbitrary 64-byte block and check if the output matches expected.
        byte[] input = new byte[64]; // all zeros
        
        // standard SHA-256 padding for empty string:
        // bit 1 followed by zeros, then length in bits (0)
        input[0] = 0x80;
        
        uint[] output = new uint[8];

        // Act
        Sha256Kernels.ComputeBlock(0, input, output);

        // Assert
        // Expected hash for empty string: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        Assert.Equal(0xe3b0c442u, output[0]);
        Assert.Equal(0x98fc1c14u, output[1]);
        Assert.Equal(0x9afbf4c8u, output[2]);
        Assert.Equal(0x996fb924u, output[3]);
        Assert.Equal(0x27ae41e4u, output[4]);
        Assert.Equal(0x649b934cu, output[5]);
        Assert.Equal(0xa495991bu, output[6]);
        Assert.Equal(0x7852b855u, output[7]);
    }
}
