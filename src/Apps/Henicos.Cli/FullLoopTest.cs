
namespace Henicos.Cli;

/// <summary>End-to-end verification test comparing CPU BigInteger ModPow against GPU hybrid reduction loops.</summary>
public class FullLoopTest
{
    public static void Run()
    {
        const int Exponent = 1000;
        var baseOffset = BigInteger.One << Exponent;
        var modulus = (BigInteger.One << (2 * Exponent + 1)) - baseOffset + 11;

        var numWords = (2 * Exponent + 1 + 31) / 32;
        var data = new uint[numWords * 2 + 16];
        data[0] = 2; // T = 2

        for (var iter = 0; iter < Exponent; iter++)
        {
            squareAndReduce();
            multiplyBy2AndReduce();
        }

        for (var iter = 0; iter < Exponent - 4; iter++)
        {
            squareAndReduce();
        }

        squareAndReduce();
        multiplyBy2AndReduce();
        squareAndReduce();
        squareAndReduce();
        multiplyBy2AndReduce();
        squareAndReduce();

        // Make data positive
        var resBytes = new byte[data.Length * 4 + 1];
        Buffer.BlockCopy(data, 0, resBytes, 0, data.Length * 4);
        resBytes[data.Length * 4] = 0;
        var resultBigInt = new BigInteger(resBytes);

        Logger<FullLoopTest>.Info($"Hybrid CPU Full Loop: {resultBigInt % modulus}");

        var expectedModPow = BigInteger.ModPow(2, modulus - 1, modulus);
        Logger<FullLoopTest>.Info($"Standard ModPow:      {expectedModPow}");
        Logger<FullLoopTest>.Info($"Match: {(resultBigInt % modulus) == expectedModPow}");
        return;

        void squareAndReduce()
        {
            var dataBytes = new byte[data.Length * 4 + 1];
            Buffer.BlockCopy(data, 0, dataBytes, 0, data.Length * 4);
            var tBig = new BigInteger(dataBytes);
            tBig *= tBig;
            var bytes = tBig.ToByteArray();
            Array.Clear(data, 0, data.Length);
            Buffer.BlockCopy(bytes, 0, data, 0, Math.Min(bytes.Length, data.Length * 4));

            HybridReduction.ReduceInPlace(data, Exponent, numWords);
        }

        void multiplyBy2AndReduce()
        {
            var dataBytes = new byte[data.Length * 4 + 1];
            Buffer.BlockCopy(data, 0, dataBytes, 0, data.Length * 4);
            var tBig = new BigInteger(dataBytes);
            tBig *= 2;
            var bytes = tBig.ToByteArray();
            Array.Clear(data, 0, data.Length);
            Buffer.BlockCopy(bytes, 0, data, 0, Math.Min(bytes.Length, data.Length * 4));

            HybridReduction.ReduceInPlace(data, Exponent, numWords);
        }
    }
}
