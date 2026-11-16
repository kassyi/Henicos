#nullable enable

using Kassyi.GSharp.Runtime.Fatbin;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Runtime.FatbinParserTests;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class FatbinCompatibilityTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(8, 0, true)]   // Exact match for sm_80 PTX
    [InlineData(8, 6, true)]   // Forward compatible (SM 8.6 can run sm_80 PTX)
    [InlineData(9, 0, true)]   // Forward compatible (SM 9.0 can run sm_80 PTX)
    [InlineData(7, 5, false)]  // Older architecture cannot run sm_80 PTX
    [InlineData(6, 1, false)]  // Older architecture cannot run sm_80 PTX
    public void IsCompatibleWith_PtxForwardCompatibility_ReturnsTrue(int deviceMajor, int deviceMinor, bool expected)
    {
        bool success = FatbinParser.TryParse(RealNvcc_DummyFatbin, out FatbinImage? image);
        Assert.True(success);
        Assert.NotNull(image);

        bool compatible = image.IsCompatibleWith(deviceMajor, deviceMinor);
        Assert.Equal(expected, compatible);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsCompatibleWith_CubinSameMajorNewerMinor_ReturnsFalse()
    {
        // Synthesize a CUBIN item (Type = 2) for sm_80
        byte[] cubinItem = CreateMockFatbin(type: FatbinItemInfo.TypeCubin, computeVersion: 80, major: 8, minor: 0);
        bool success = FatbinParser.TryParse(cubinItem, out FatbinImage? image);
        Assert.True(success);
        Assert.NotNull(image);

        // A cubin runs on devices of the same major version with an equal or newer minor version
        Assert.True(image.IsCompatibleWith(8, 0));
        Assert.True(image.IsCompatibleWith(8, 6));
        Assert.False(image.IsCompatibleWith(9, 0)); // Another major version needs PTX
        Assert.False(image.IsCompatibleWith(7, 5));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(8, 0, 8, 0, true)]  // Exact match
    [InlineData(8, 0, 8, 6, true)]  // Newer minor
    [InlineData(8, 0, 9, 0, true)]  // Newer major
    [InlineData(8, 6, 8, 0, false)] // Target older minor
    [InlineData(8, 0, 7, 5, false)] // Target older major
    [InlineData(8, 6, 8, 6, true)]  // Default compute_86 PTX on its own architecture
    [InlineData(8, 6, 9, 0, true)]  // Default compute_86 PTX is JIT-compiled on Hopper
    [InlineData(8, 6, 12, 0, true)] // Default compute_86 PTX is JIT-compiled on Blackwell
    public void IsCompatibleWith_PtxTarget_PermitsForwardCompatibility(
        ushort ptxMajor,
        ushort ptxMinor,
        int deviceMajor,
        int deviceMinor,
        bool expected)
    {
        byte[] fatbinBytes = CreateMockFatbin(
            FatbinItemInfo.TypePtx,
            (uint)(ptxMajor * 10 + ptxMinor),
            ptxMajor,
            ptxMinor
        );
        bool parsed = FatbinParser.TryParse(fatbinBytes, out FatbinImage? image);

        Assert.True(parsed);
        Assert.NotNull(image);
        Assert.Equal(expected, image.IsCompatibleWith(deviceMajor, deviceMinor));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(8, 6, 8, 6, true)]   // Exact match
    [InlineData(8, 6, 8, 9, true)]   // Newer minor of the same major (sm_86 cubin on an RTX 40xx)
    [InlineData(8, 0, 8, 6, true)]   // Newer minor of the same major
    [InlineData(8, 6, 8, 0, false)]  // Older minor
    [InlineData(8, 6, 9, 0, false)]  // Newer major (a cubin is not forward compatible across major versions)
    [InlineData(8, 6, 12, 0, false)] // Newer major
    [InlineData(8, 0, 7, 5, false)]  // Older major
    public void IsCompatibleWith_CubinSameMajorNewerMinorVersion_RejectsIncompatible(
        ushort cubinMajor,
        ushort cubinMinor,
        int deviceMajor,
        int deviceMinor,
        bool expected)
    {
        byte[] fatbinBytes = CreateMockFatbin(
            FatbinItemInfo.TypeCubin,
            (uint)(cubinMajor * 10 + cubinMinor),
            cubinMajor,
            cubinMinor
        );
        bool parsed = FatbinParser.TryParse(fatbinBytes, out FatbinImage? image);

        Assert.True(parsed);
        Assert.NotNull(image);
        Assert.Equal(expected, image.IsCompatibleWith(deviceMajor, deviceMinor));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1, 0)]
    [InlineData(8, -1)]
    [InlineData(-1, -1)]
    public void IsCompatibleWith_NegativeArchitecture_ReturnsFalse(int deviceMajor, int deviceMinor)
    {
        byte[] fatbinBytes = CreateMockFatbin(FatbinItemInfo.TypePtx, 80, 8, 0);
        bool parsed = FatbinParser.TryParse(fatbinBytes, out FatbinImage? image);

        Assert.True(parsed);
        Assert.NotNull(image);
        Assert.False(image.IsCompatibleWith(deviceMajor, deviceMinor));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsCompatibleWith_MultipleFatbinItems_MatchesAnyCompatibleItem()
    {
        // FATBIN containing sm_86 CUBIN and sm_80 PTX
        byte[] fatbinBytes = CreateMockFatbin(
            (FatbinItemInfo.TypeCubin, 86u, (ushort)8, (ushort)6),
            (FatbinItemInfo.TypePtx, 80u, (ushort)8, (ushort)0)
        );

        bool parsed = FatbinParser.TryParse(fatbinBytes, out FatbinImage? image);
        Assert.True(parsed);
        Assert.NotNull(image);

        // On sm_86: Matches CUBIN (and PTX)
        Assert.True(image.IsCompatibleWith(8, 6));

        // On sm_80: Matches PTX
        Assert.True(image.IsCompatibleWith(8, 0));

        // On sm_90: Matches PTX forward compatibility
        Assert.True(image.IsCompatibleWith(9, 0));

        // On sm_75: Neither matches
        Assert.False(image.IsCompatibleWith(7, 5));
    }
}
