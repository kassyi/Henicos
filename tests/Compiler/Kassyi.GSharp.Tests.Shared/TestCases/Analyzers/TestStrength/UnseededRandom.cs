using System;
using System.Security.Cryptography;
using Xunit;

namespace TestStrengthCases
{
    public class UnseededRandomTests
    {
        private static readonly Random s_shared = new Random();

        private static readonly Random s_seeded = new Random(42);

        [Fact]
        public void CreatesUnseededRandom()
        {
            var random = new Random();
            Assert.InRange(random.Next(10), 0, 9);
        }

        [Fact]
        public void CreatesTargetTypedUnseededRandom()
        {
            Random random = new();
            Assert.InRange(random.Next(10), 0, 9);
        }

        [Fact]
        public void CreatesSeededRandom()
        {
            var random = new Random(seed: 7);
            Assert.InRange(random.Next(10), 0, 9);
        }

        [Fact]
        public void CreatesUserTypeNamedRandom()
        {
            var random = new UserRandom.Random();
            Assert.NotNull(random);
        }

        [Fact]
        public void UsesSharedRandom()
        {
            Assert.InRange(Random.Shared.Next(10), 0, 9);
        }

        [Fact]
        public void UsesCryptographicRandom()
        {
            var bytes = new byte[4];
            RandomNumberGenerator.Fill(bytes);
            using var generator = RandomNumberGenerator.Create();
            generator.GetBytes(bytes);
            Assert.InRange(RandomNumberGenerator.GetInt32(10), 0, 9);
        }

        [Fact]
        public void UsesUserTypeNamedRandomNumberGenerator()
        {
            Assert.Equal(4, UserRandom.RandomNumberGenerator.GetInt32(10));
        }
    }
}

namespace UserRandom
{
    public class Random
    {
    }

    public static class RandomNumberGenerator
    {
        public static int GetInt32(int toExclusive) => 4;
    }
}
