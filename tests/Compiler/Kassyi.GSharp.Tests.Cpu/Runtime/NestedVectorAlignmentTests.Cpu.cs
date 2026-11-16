using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

public partial class NestedVectorAlignmentTests
{
    private partial void AssertOffsetsAndSize(int offset1, int offset2, int offset3, int offset4, int offset5, int totalSize)
    {
        Assert.Equal(0, offset1);
        Assert.Equal(4, offset2);
        Assert.Equal(12, offset3);
        Assert.Equal(28, offset4);
        Assert.Equal(32, offset5);
        Assert.Equal(48, totalSize);
    }
}
