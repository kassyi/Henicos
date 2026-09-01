using System;
using Kassyi.GSharp.Compiler.Dialects;
using Xunit;

namespace Kassyi.GSharp.Tests.DialectTests
{
    public class YamlDialectTests
    {
        [Fact]
        public void CanLoadStandardCpp()
        {
            var dialect = new StandardCppDialect();
            Assert.NotNull(dialect);

            bool result = dialect.TryMapType("System.Int32", out string mappedType);

            Assert.True(result);
            Assert.Equal("int", mappedType);
        }

        [Fact]
        public void CanLoadGSharpCuda()
        {
            var dialect = new GSharpCudaDialect();
            Assert.NotNull(dialect);

            // Check MethodPrefix
            Assert.Equal("__device__ inline ", dialect.MethodPrefix);
        }
    }
}
