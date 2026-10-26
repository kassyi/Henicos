using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace TestStrengthCases
{
    public class BroadCatchTests
    {
        [Fact]
        public void CatchesEverything()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch
            {
            }
        }

        [Fact]
        public void CatchesException()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch (Exception)
            {
                return;
            }
        }

        [Fact]
        public void CatchesQualifiedExceptionInLambda()
        {
            Action run = () =>
            {
                try
                {
                    Assert.Equal(2, Compute(1));
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine(ex);
                }
            };
            run();
        }

        [Fact]
        public void CatchesExceptionWithFilter()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
                return;
            }
        }

        [Fact]
        public void CatchesNarrowType()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }

        [Fact]
        public void CatchesExceptionAndRethrows()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }

        [Fact]
        public void RecordsExceptionByAssignment()
        {
            Exception captured = null;
            try
            {
                Compute(1);
            }
            catch (Exception ex)
            {
                captured = ex;
            }

            Assert.Null(captured);
        }

        [Fact]
        public void RecordsExceptionInList()
        {
            var errors = new List<Exception>();
            try
            {
                Compute(1);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }

            Assert.Empty(errors);
        }

        [Fact]
        public void RecordsExceptionMessageAndContinues()
        {
            var errors = new List<string>();
            for (var i = 0; i < 2; i++)
            {
                try
                {
                    Compute(i);
                }
                catch (Exception ex)
                {
                    errors.Add($"case {i}: {ex.Message}");
                    continue;
                }
            }

            Assert.Empty(errors);
        }

        [Fact]
        public void WrapsAndThrows()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("wrapped", ex);
            }
        }

        [Fact]
        public void AssertsInHandler()
        {
            try
            {
                Compute(1);
            }
            catch (Exception ex)
            {
                Assert.IsType<InvalidOperationException>(ex);
            }
        }

        [Fact]
        public void WritesExceptionToTestOutput()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch (Exception ex)
            {
                _output.WriteLine(ex.ToString());
            }
        }

        [Fact]
        public void WritesExceptionToDebugAndTrace()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Trace.WriteLine(ex);
            }
        }

        [Fact]
        public void ReturnsBeforeRethrow()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch (Exception)
            {
                if (Compute(0) == 0)
                {
                    return;
                }

                throw;
            }
        }

        [Fact]
        public void SkipsInsteadOfFailing()
        {
            try
            {
                Assert.Equal(2, Compute(1));
            }
            catch (Exception ex)
            {
                throw new SkipException(ex.Message);
            }
        }

        private readonly ITestOutputHelper _output;

        public BroadCatchTests(ITestOutputHelper output) => _output = output;

        private static int Compute(int value)
        {
            try
            {
                return value * 2;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
