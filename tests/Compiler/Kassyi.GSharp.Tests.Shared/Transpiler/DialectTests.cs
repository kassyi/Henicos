namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public class DialectTests
{
        [Fact]
        public void Test_CanLoadStandardCpp()
        {
            var dialect = new StandardCppDialect();
            Assert.NotNull(dialect);

            bool result = dialect.TryMapType("System.Int32", out string mappedType);

            Assert.True(result);
            Assert.Equal("int", mappedType);
        }

        [Fact]
        public void Test_CanLoadGSharpCuda()
        {
            var dialect = new GSharpCudaDialect();
            Assert.NotNull(dialect);

            // Check MethodPrefix
            Assert.Equal("__device__ inline ", dialect.MethodPrefix);
        }
    }
