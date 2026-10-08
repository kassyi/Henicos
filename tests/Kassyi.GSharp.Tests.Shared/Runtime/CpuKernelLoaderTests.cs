using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Kassyi.GSharp.Cryptography;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[Trait("Category", "CpuExecution")]
public class CpuKernelLoaderTests
{
    private delegate void EmptyDelegate();

    [Fact]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_CpuLoader_ShortBuffer_ThrowsInvalidOperationException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[63]); // Less than 64 bytes
            
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
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_CpuLoader_HashMismatch_ThrowsInvalidOperationException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            byte[] dummyBinary = "INVALID_DLL_CONTENT"u8.ToArray();
            byte[] invalidSignature = new byte[64]; // all zeros, incorrect
            byte[] fileContent = [..dummyBinary, ..invalidSignature];
            File.WriteAllBytes(tempFile, fileContent);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                CpuKernelLoader.LoadAotKernel<EmptyDelegate>("dummy_kernel", tempFile);
            });
            Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void Test_CpuLoader_ValidSignature_FailsAtNativeLibraryLoad()
    {
        var tempFile = Path.GetTempFileName();
        var dllFile = tempFile + ".dll";
        try
        {
            byte[] dummyBinary = "INVALID_DLL_CONTENT"u8.ToArray();
            byte[] validSignature = Ed25519.Sign(dummyBinary, Ed25519.DefaultDevKeySeed);
            byte[] fileContent = [..dummyBinary, ..validSignature];
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
