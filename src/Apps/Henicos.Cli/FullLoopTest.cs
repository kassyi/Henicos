using System.Numerics;

namespace Henicos.Cli;

public static class FullLoopTest
{
    public static void Run()
    {
        var E = 1000;
        var X = BigInteger.One << E;
        var N = (BigInteger.One << (2 * E + 1)) - X + 11;

        var numWords = (2 * E + 1 + 31) / 32;
        var data = new uint[numWords * 2 + 16];
        data[0] = 2; // T = 2

        var squareAndReduce = () =>
        {
            var dataBytes = new byte[data.Length * 4 + 1];
            Buffer.BlockCopy(data, 0, dataBytes, 0, data.Length * 4);
            var tBig = new BigInteger(dataBytes);
            tBig = tBig * tBig;
            var bytes = tBig.ToByteArray();
            Array.Clear(data, 0, data.Length);
            Buffer.BlockCopy(bytes, 0, data, 0, System.Math.Min(bytes.Length, data.Length * 4));

            HybridReduction.ReduceInPlace(data, E, numWords);
        };

        var multiplyBy2AndReduce = () =>
        {
            var dataBytes = new byte[data.Length * 4 + 1];
            Buffer.BlockCopy(data, 0, dataBytes, 0, data.Length * 4);
            var tBig = new BigInteger(dataBytes);
            tBig = tBig * 2;
            var bytes = tBig.ToByteArray();
            Array.Clear(data, 0, data.Length);
            Buffer.BlockCopy(bytes, 0, data, 0, System.Math.Min(bytes.Length, data.Length * 4));

            HybridReduction.ReduceInPlace(data, E, numWords);
        };

        for (var iter = 0; iter < E; iter++)
        {
            squareAndReduce();
            multiplyBy2AndReduce();
        }
        for (var iter = 0; iter < E - 4; iter++)
        {
            squareAndReduce();
        }

        squareAndReduce(); multiplyBy2AndReduce();
        squareAndReduce();
        squareAndReduce(); multiplyBy2AndReduce();
        squareAndReduce();

        // Make data positive
        var resBytes = new byte[data.Length * 4 + 1];
        Buffer.BlockCopy(data, 0, resBytes, 0, data.Length * 4);
        resBytes[data.Length * 4] = 0;
        var T = new BigInteger(resBytes);

        Console.WriteLine($"Hybrid CPU Full Loop: {T % N}");

        var std = BigInteger.ModPow(2, N - 1, N);
        Console.WriteLine($"Standard ModPow:      {std}");
        Console.WriteLine($"Match: {(T % N) == std}");
    }
}
