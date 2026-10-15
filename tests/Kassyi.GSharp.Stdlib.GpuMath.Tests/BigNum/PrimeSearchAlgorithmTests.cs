
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.CpuExecution)]
public class PrimeSearchAlgorithmTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(32)]
    [InlineData(64)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PrimeSearch_ValidBitLengths(int bitLength)
    {
        var prime = PrimeSearchAlgorithm.Generate(bitLength);
        Assert.Equal(bitLength, prime.GetBitLength());
        Assert.True(IsDeterministicOrProbablePrime(prime), $"Generated value {prime} is not prime.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-8)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Test_PrimeSearch_InvalidBitLength_ThrowsArgumentOutOfRangeException(int invalidBitLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrimeSearchAlgorithm.Generate(invalidBitLength));
    }

    private static bool IsDeterministicOrProbablePrime(BigInteger n)
    {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n.IsEven) return false;

        // Miller-Rabin deterministic bases for 64-bit integers: { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 }
        BigInteger d = n - 1;
        int s = 0;
        while (d.IsEven)
        {
            d >>= 1;
            s++;
        }

        ReadOnlySpan<int> bases = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37];
        foreach (int aVal in bases)
        {
            if (n <= aVal) break;
            BigInteger a = aVal;
            BigInteger x = BigInteger.ModPow(a, d, n);
            if (x == 1 || x == n - 1) continue;

            bool composite = true;
            for (int r = 1; r < s; r++)
            {
                x = BigInteger.ModPow(x, 2, n);
                if (x == n - 1)
                {
                    composite = false;
                    break;
                }
            }

            if (composite) return false;
        }

        return true;
    }
}
