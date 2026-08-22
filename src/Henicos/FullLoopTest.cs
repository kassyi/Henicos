using System;
using System.Numerics;
using System.Diagnostics;
using Kassyi.Math.GpuBigInt;

namespace Henicos {
    public static class FullLoopTest {
        public static void Run() {
            int E = 1000;
            BigInteger X = BigInteger.One << E;
            BigInteger N = (BigInteger.One << (2 * E + 1)) - X + 11;

            int numWords = (2 * E + 1 + 31) / 32;
            uint[] data = new uint[numWords * 2 + 16];
            data[0] = 2; // T = 2

            Action squareAndReduce = () => {
                byte[] dataBytes = new byte[data.Length * 4 + 1];
                Buffer.BlockCopy(data, 0, dataBytes, 0, data.Length * 4);
                BigInteger tBig = new BigInteger(dataBytes);
                tBig = tBig * tBig;
                byte[] bytes = tBig.ToByteArray();
                Array.Clear(data, 0, data.Length);
                Buffer.BlockCopy(bytes, 0, data, 0, Math.Min(bytes.Length, data.Length * 4));
                
                HybridReduction.ReduceInPlace(data, E, numWords);
            };

            Action multiplyBy2AndReduce = () => {
                byte[] dataBytes = new byte[data.Length * 4 + 1];
                Buffer.BlockCopy(data, 0, dataBytes, 0, data.Length * 4);
                BigInteger tBig = new BigInteger(dataBytes);
                tBig = tBig * 2;
                byte[] bytes = tBig.ToByteArray();
                Array.Clear(data, 0, data.Length);
                Buffer.BlockCopy(bytes, 0, data, 0, Math.Min(bytes.Length, data.Length * 4));
                
                HybridReduction.ReduceInPlace(data, E, numWords);
            };

            for (int iter = 0; iter < E; iter++) {
                squareAndReduce();
                multiplyBy2AndReduce();
            }
            for (int iter = 0; iter < E - 4; iter++) {
                squareAndReduce();
            }
            
            squareAndReduce(); multiplyBy2AndReduce();
            squareAndReduce();
            squareAndReduce(); multiplyBy2AndReduce();
            squareAndReduce();

            // Make data positive
            byte[] resBytes = new byte[data.Length * 4 + 1];
            Buffer.BlockCopy(data, 0, resBytes, 0, data.Length * 4);
            resBytes[data.Length * 4] = 0;
            BigInteger T = new BigInteger(resBytes);
            
            Console.WriteLine($"Hybrid CPU Full Loop: {T % N}");

            BigInteger std = BigInteger.ModPow(2, N - 1, N);
            Console.WriteLine($"Standard ModPow:      {std}");
            Console.WriteLine($"Match: {(T % N) == std}");
        }
    }
}
