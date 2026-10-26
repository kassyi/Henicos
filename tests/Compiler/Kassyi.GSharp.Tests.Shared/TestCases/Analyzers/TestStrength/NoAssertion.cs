using System;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

namespace TestStrengthCases
{
    public abstract class SnapshotBase
    {
        protected Task VerifySnapshot(string text) => Verifier.Verify(text);
    }

    public class NoAssertionTests : SnapshotBase
    {
        [Fact]
        public void CallsOnly()
        {
            Compute(1);
        }

        [Fact]
        public void AssertsDirectly()
        {
            Assert.Equal(2, Compute(1));
        }

        [Fact]
        public void AssertsThroughThrows()
        {
            Assert.Throws<InvalidOperationException>(() => Compute(-1));
        }

        [Fact]
        public void AssertsThroughHelperChain()
        {
            CheckOuter(1);
        }

        [Fact]
        public void CallsHelperThatChecksNothing()
        {
            Log(Compute(1));
        }

        [Fact]
        public void AssertsInLocalFunction()
        {
            Check(Compute(1));

            static void Check(int value) => Assert.True(value > 0);
        }

        [Fact]
        public void PassesCheckingMethodGroup()
        {
            Run(CheckInner);
        }

        [Fact]
        public Task VerifiesSnapshot() => Verifier.Verify(Compute(1));

        [Fact]
        public Task VerifiesThroughBaseClass() => VerifySnapshot("x");

        [Fact]
        public void CallsRecursiveHelperThatChecksNothing()
        {
            Recurse(3);
        }

        [Theory]
        [InlineData(1)]
        public void TheoryCallsOnly(int value)
        {
            Compute(value);
        }

        [SkippableFact]
        public void SkippableFactCallsOnly()
        {
            Compute(1);
        }

        [Fact]
        public void CallsUserTypeNamedAssert()
        {
            UserAsserts.Assert.Equal(2, Compute(1));
        }

        [Fact]
        public void AssertsWithFluentAssertions()
        {
            FluentAssertions.FakeShould.Be(Compute(1), 2);
        }

        public void NotATestChecksNothing()
        {
            Compute(1);
        }

        private static int Compute(int value) =>
            value < 0 ? throw new InvalidOperationException() : value * 2;

        private static void CheckOuter(int value) => CheckInner(Compute(value));

        private static void CheckInner(int value) => Assert.Equal(2, value);

        private static void Log(int value) => Console.WriteLine(value);

        private static void Run(Action<int> check) => check(Compute(1));

        private static void Recurse(int depth)
        {
            if (depth > 0)
            {
                Recurse(depth - 1);
            }
        }
    }

    public abstract class CheckingBase
    {
        // Non-virtual entry point -> abstract check -> the override asserts
        protected void RunCheck() => Check(2);

        protected abstract void Check(int value);
    }

    public interface IChecker
    {
        void Check(int value);
    }

    public interface IReporter
    {
        void Report(int value);
    }

    public sealed class ImplicitChecker : IChecker
    {
        public void Check(int value) => Assert.Equal(2, value);
    }

    public sealed class ExplicitChecker : IChecker
    {
        void IChecker.Check(int value) => Assert.Equal(2, value);
    }

    public sealed class SilentReporter : IReporter
    {
        public void Report(int value) => Console.WriteLine(value);
    }

    public class DispatchedAssertionTests : CheckingBase
    {
        [Fact]
        public void AssertsThroughAbstractOverride() => RunCheck();

        [Fact]
        public void AssertsThroughImplicitInterfaceImplementation()
        {
            IChecker checker = new ImplicitChecker();
            checker.Check(2);
        }

        [Fact]
        public void AssertsThroughExplicitInterfaceImplementation()
        {
            IChecker checker = new ExplicitChecker();
            checker.Check(2);
        }

        [Fact]
        public void CallsInterfaceWhoseImplementationsCheckNothing()
        {
            IReporter reporter = new SilentReporter();
            reporter.Report(2);
        }

        protected override void Check(int value) => Assert.Equal(2, value);
    }
}

namespace UserAsserts
{
    public static class Assert
    {
        public static void Equal(int expected, int actual)
        {
        }
    }
}

namespace FluentAssertions
{
    public static class FakeShould
    {
        public static void Be(int actual, int expected)
        {
        }
    }
}
