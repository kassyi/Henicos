using System;
using System.Numerics;

namespace Henicos;

public static class ModularReduction2
{
    /// <summary>
    /// N = 2^{2E+1} - 2^E + 11 に対する超最適化リダクション
    /// 多倍長乗算(FFT)を完全に排除し、O(L) のシフトと加減算のみで剰余を求めます。
    /// </summary>
    public static BigInteger ReduceOptimized(BigInteger y, int E, BigInteger N)
    {
        int shiftAmount = 2 * E + 1;

        while (y >= N)
        {
            // Y = Q * 2^{2E+1} + R
            BigInteger q = y >> shiftAmount;
            
            if (q.IsZero)
            {
                // Q == 0 かつ Y >= N の極小隙間に落ちた場合の最終調整
                y -= N;
                continue;
            }

            // R = Y mod 2^{2E+1} (ビットマスク抽出)
            BigInteger r = y - (q << shiftAmount);

            // 【究極最適化】 Q * (2^E - 11) をシフトとスカラー加減算 O(L) に分解
            // Y' = (Q << E) + R - 11 * Q
            // ※GPU実装時は 11*Q を (Q<<3) + (Q<<1) + Q のシフト加算に展開します
            y = (q << E) + r - (q * 11);
        }

        return y;
    }

    /// <summary>
    /// N-1 の既知の固定ビット構造を利用した、完全分岐レスの特化型 Fermat PRP テスト
    /// </summary>
    public static BigInteger FermatPRPTest(int E, BigInteger N)
    {
        // 最初のビット(MSB)の '1' を初期値としてセット (Base = 2)
        BigInteger result = 2; 

        // 1. 上位の '1' の連続 (初期化で1つ消費したため、残りは E 個)
        // 動作: Square & Multiply (Base=2 なので Multiply は << 1 と -N のみ)
        for (int i = 0; i < E; i++)
        {
            result = ReduceOptimized(result * result, E, N); // Square (唯一の重いFFT処理)
            
            result <<= 1; // Multiply by 2 (完全 O(L) 時間)
            if (result >= N) result -= N;
        }

        // 2. 中位の '0' の連続 (E - 4 個)
        // 動作: Square Only
        for (int i = 0; i < E - 4; i++)
        {
            result = ReduceOptimized(result * result, E, N);
        }

        // 3. 下位 4ビット "1010" 
        // Bit '1'
        result = ReduceOptimized(result * result, E, N);
        result <<= 1; if (result >= N) result -= N;
        // Bit '0'
        result = ReduceOptimized(result * result, E, N);
        // Bit '1'
        result = ReduceOptimized(result * result, E, N);
        result <<= 1; if (result >= N) result -= N;
        // Bit '0' (LSB)
        result = ReduceOptimized(result * result, E, N);

        return result; // 結果が 1 なら確率的素数 (PRP)
    }
}