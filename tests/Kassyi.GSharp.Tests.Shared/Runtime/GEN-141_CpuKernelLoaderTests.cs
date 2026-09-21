using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Runtime;

[Trait("Category", "CpuExecution")]
public class GEN141_CpuKernelLoaderTests
{
    private delegate void EmptyDelegate();

    [Fact]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN141_CpuLoader_ShortBuffer_ThrowsInvalidOperationException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[31]); // Less than 32 bytes
            
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                CpuKernelLoader.LoadAotKernelPtr("dummy_kernel", tempFile);
            });
            Assert.Contains("too small", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN141_CpuLoader_HashMismatch_ThrowsInvalidOperationException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            byte[] dummyBinary = "INVALID_DLL_CONTENT"u8.ToArray();
            byte[] invalidHash = new byte[32]; // all zeros, incorrect
            byte[] fileContent = [..dummyBinary, ..invalidHash];
            File.WriteAllBytes(tempFile, fileContent);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                CpuKernelLoader.LoadAotKernel<EmptyDelegate>("dummy_kernel", tempFile);
            });
            Assert.Contains("hash mismatch", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait("FeatureId", "GEN-141")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_GEN141_CpuLoader_ValidHash_FailsAtNativeLibraryLoad()
    {
        var tempFile = Path.GetTempFileName();
        var dllFile = tempFile + ".dll";
        try
        {
            byte[] dummyBinary = "INVALID_DLL_CONTENT"u8.ToArray();
            byte[] validHash = SHA256.HashData(dummyBinary);
            byte[] fileContent = [..dummyBinary, ..validHash];
            File.WriteAllBytes(dllFile, fileContent);

            Assert.ThrowsAny<Exception>(() =>
            {
                CpuKernelLoader.LoadAotKernelPtr("dummy_kernel", dllFile);
            });
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(dllFile)) File.Delete(dllFile);
        }
    }
}
