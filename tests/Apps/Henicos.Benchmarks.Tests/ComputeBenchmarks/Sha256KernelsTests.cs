using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Henicos.Benchmarks.Tests.ComputeBenchmarks;

[Category(TestCategory.Runtime)]
public class Sha256KernelsTests
{
    [Fact]
    public void ComputeBlock_MatchesStandardSha256()
    {
        // Arrange: Standard SHA-256 padding for empty string
        byte[] input = new byte[64];
        input[0] = 0x80;
        uint[] output = new uint[8];

        // Act
        Sha256Kernels.ComputeBlock(0, input, output);

        // Assert
        Assert.Equal(0xe3b0c442u, output[0]);
        Assert.Equal(0x98fc1c14u, output[1]);
        Assert.Equal(0x9afbf4c8u, output[2]);
        Assert.Equal(0x996fb924u, output[3]);
        Assert.Equal(0x27ae41e4u, output[4]);
        Assert.Equal(0x649b934cu, output[5]);
        Assert.Equal(0xa495991bu, output[6]);
        Assert.Equal(0x7852b855u, output[7]);
    }

    [Fact]
    public void ComputeBlock_MultiMessage_NonZeroPayloads_MatchesStandardSha256()
    {
        const int numHashes = 64;
        byte[] input = new byte[numHashes * 64];
        uint[] output = new uint[numHashes * 8];

        string[] messages = new string[numHashes];
        for (int tid = 0; tid < numHashes; tid++)
        {
            messages[tid] = tid == 0 ? "abc" : $"GSharp-SHA256-TestVector-{tid:D4}";
            byte[] msgBytes = Encoding.ASCII.GetBytes(messages[tid]);
            int blockStart = tid * 64;
            Buffer.BlockCopy(msgBytes, 0, input, blockStart, msgBytes.Length);
            // Append SHA-256 1-bit padding (0x80) and 64-bit big-endian bit length at offset 56..63
            input[blockStart + msgBytes.Length] = 0x80;
            BinaryPrimitives.WriteUInt64BigEndian(input.AsSpan(blockStart + 56, 8), (ulong)(msgBytes.Length * 8));
        }

        for (int tid = 0; tid < numHashes; tid++)
        {
            Sha256Kernels.ComputeBlock(tid, input, output);
        }

        for (int tid = 0; tid < numHashes; tid++)
        {
            byte[] expectedHash = SHA256.HashData(Encoding.ASCII.GetBytes(messages[tid]));
            for (int w = 0; w < 8; w++)
            {
                uint expectedWord = BinaryPrimitives.ReadUInt32BigEndian(expectedHash.AsSpan(w * 4, 4));
                Assert.Equal(expectedWord, output[tid * 8 + w]);
            }
        }
    }
}
