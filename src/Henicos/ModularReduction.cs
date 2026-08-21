using System;
using System.Numerics;

namespace Henicos;

public static class ModularReduction
{
    /// <summary>
    /// N = 2*(2^E)^2 - 2^E + 11 に対して、専用のリダクション Y mod N を計算します。
    /// 2^{2E+1} ≡ 2^E - 11 (mod N) の関係を利用して、剰余を求めます。
    /// </summary>
    public static BigInteger Reduce(BigInteger y, int E, BigInteger N)
    {
        int shiftAmount = 2 * E + 1;
        BigInteger multiplier = (BigInteger.One << E) - 11; // 2^E - 11

        while (y >= N)
        {
            // y = Q * 2^{2E+1} + R
            BigInteger q = y >> shiftAmount;
            
            if (q == BigInteger.Zero)
            {
                // Q == 0 ということは y < 2^{2E+1} であるが y >= N の状態
                y -= N;
                continue;
            }

            // R = y - Q * 2^{2E+1}
            BigInteger r = y - (q << shiftAmount);

            // y' = Q * (2^E - 11) + R
            y = q * multiplier + r;
        }

        return y;
    }

    /// <summary>
    /// カスタムのリダクションを利用した Modular Exponentiation: (baseValue ^ exponent) mod N
    /// </summary>
    public static BigInteger ModPowCustom(BigInteger baseValue, BigInteger exponent, int E, BigInteger N)
    {
        BigInteger result = BigInteger.One;
        BigInteger currentBase = baseValue % N;
        
        byte[] exponentBytes = exponent.ToByteArray();
        
        for (int i = 0; i < exponentBytes.Length; i++)
        {
            byte b = exponentBytes[i];
            for (int j = 0; j < 8; j++)
            {
                // ToByteArray は符号ビットのために余分なゼロバイトが最後に追加される場合がある。
                // 完全に exponent を超えたら終了する最適化が必要。
                BigInteger currentBitValue = BigInteger.One << (i * 8 + j);
                if (currentBitValue > exponent)
                {
                    return result;
                }

                if ((b & (1 << j)) != 0)
                {
                    result = Reduce(result * currentBase, E, N);
                }
                
                currentBase = Reduce(currentBase * currentBase, E, N);
            }
        }

        return result;
    }
}
