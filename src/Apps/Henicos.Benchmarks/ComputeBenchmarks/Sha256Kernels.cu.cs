namespace Henicos.Benchmarks.ComputeBenchmarks;

public static class Sha256Kernels
{
    // Right Rotate & SHA-256 primitives
    [GpuDeviceFunction]
    private static uint RotR(uint x, int n) => (x >> n) | (x << (32 - n));

    [GpuDeviceFunction]
    private static uint Ch(uint x, uint y, uint z) => (x & y) ^ (~x & z);

    [GpuDeviceFunction]
    private static uint Maj(uint x, uint y, uint z) => (x & y) ^ (x & z) ^ (y & z);

    [GpuDeviceFunction]
    private static uint Sigma0(uint x) => RotR(x, 2) ^ RotR(x, 13) ^ RotR(x, 22);

    [GpuDeviceFunction]
    private static uint Sigma1(uint x) => RotR(x, 6) ^ RotR(x, 11) ^ RotR(x, 25);

    [GpuDeviceFunction]
    private static uint sigma0(uint x) => RotR(x, 7) ^ RotR(x, 18) ^ (x >> 3);

    [GpuDeviceFunction]
    private static uint sigma1(uint x) => RotR(x, 17) ^ RotR(x, 19) ^ (x >> 10);

    [GpuKernel]
    public static void Sha256Kernel(byte[] input, uint[] output, int numMessages)
    {
        Gpu.GridStrideLoop(numMessages, tid =>
        {
            Span<uint> K = stackalloc uint[64]
            {
                0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
                0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
                0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
                0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
                0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
                0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
                0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
                0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
            };

            // Message offset
            int offset = tid * 64;

            Span<uint> w = stackalloc uint[64];

            // Load message to w[0..15]
            for (int i = 0; i < 16; i++)
            {
                w[i] = (uint)(input[offset + i * 4] << 24) |
                       (uint)(input[offset + i * 4 + 1] << 16) |
                       (uint)(input[offset + i * 4 + 2] << 8) |
                       (uint)(input[offset + i * 4 + 3]);
            }

            for (int i = 16; i < 64; i++)
            {
                w[i] = sigma1(w[i - 2]) + w[i - 7] + sigma0(w[i - 15]) + w[i - 16];
            }

            uint a = 0x6a09e667;
            uint b = 0xbb67ae85;
            uint c = 0x3c6ef372;
            uint d = 0xa54ff53a;
            uint e = 0x510e527f;
            uint f = 0x9b05688c;
            uint g = 0x1f83d9ab;
            uint h = 0x5be0cd19;

            for (int i = 0; i < 64; i++)
            {
                uint t1 = h + Sigma1(e) + Ch(e, f, g) + K[i] + w[i];
                uint t2 = Sigma0(a) + Maj(a, b, c);
                h = g;
                g = f;
                f = e;
                e = d + t1;
                d = c;
                c = b;
                b = a;
                a = t1 + t2;
            }

            output[tid * 8 + 0] = a + 0x6a09e667;
            output[tid * 8 + 1] = b + 0xbb67ae85;
            output[tid * 8 + 2] = c + 0x3c6ef372;
            output[tid * 8 + 3] = d + 0xa54ff53a;
            output[tid * 8 + 4] = e + 0x510e527f;
            output[tid * 8 + 5] = f + 0x9b05688c;
            output[tid * 8 + 6] = g + 0x1f83d9ab;
            output[tid * 8 + 7] = h + 0x5be0cd19;
        });
    }
}
