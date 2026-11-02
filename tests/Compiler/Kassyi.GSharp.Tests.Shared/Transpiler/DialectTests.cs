namespace Kassyi.GSharp.Tests.Shared.Transpiler;

[Category(TestCategory.Compiler)]
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

            Assert.Equal("StandardCppDialect", dialect.Name);
            Assert.False(dialect.IsCuda);
            Assert.True(dialect.EmulatesThreadsWithStateMachine());
            Assert.False(DialectCallSupport.SupportsInlinePtx(dialect));
            Assert.Equal(
                "Gpu.InlinePtx is not supported by the StandardCppDialect dialect",
                DialectCallSupport.GetInlinePtxUnsupportedMessage(dialect.Name));
        }

        [Fact]
        public void Test_CanLoadGSharpCuda()
        {
            var dialect = new GSharpCudaDialect();
            Assert.NotNull(dialect);

            // Backend and capabilities come from GSharpCuda.yml
            Assert.Equal("GSharpCudaDialect", dialect.Name);
            Assert.True(dialect.IsCuda);
            Assert.False(dialect.EmulatesThreadsWithStateMachine());
            Assert.True(DialectCallSupport.SupportsInlinePtx(dialect));
        }
    }
