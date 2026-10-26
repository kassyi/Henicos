using System;
using System.Diagnostics;
using Xunit;

namespace TestStrengthCases
{
    public class DebugAssertTests
    {
        [Fact]
        public void UsesDebugAssert()
        {
            Debug.Assert(Compute(1) == 2);
        }

        [Fact]
        public void UsesDebugAssertWithMessageInLambda()
        {
            Action check = () => System.Diagnostics.Debug.Assert(Compute(1) == 2, "doubled");
            check();
            Assert.Equal(2, Compute(1));
        }

        [Fact]
        public void UsesXunitAssert()
        {
            Assert.Equal(2, Compute(1));
        }

        [Fact]
        public void UsesUserTypeNamedDebug()
        {
            UserDiagnostics.Debug.Assert(Compute(1) == 2);
            Assert.Equal(2, Compute(1));
        }

        private static int Compute(int value)
        {
            Debug.Assert(value >= 0);
            return value * 2;
        }
    }
}

namespace UserDiagnostics
{
    public static class Debug
    {
        public static void Assert(bool condition)
        {
        }
    }
}
