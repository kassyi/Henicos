using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Kassyi.GSharp.Cryptography;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[TestTarget(ExecutionTarget.CpuExecution)]
public class CpuKernelLoaderTests
{
    private delegate void EmptyDelegate();

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
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
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
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
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetTickCountDelegate();

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CpuLoader_LoadPtrAndDelegate_SameExport_DoesNotCollide()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var systemDll = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
        if (!File.Exists(systemDll))
        {
            return;
        }

        var tempDll = Path.Combine(Path.GetTempPath(), $"gsharp_test_{Guid.NewGuid():N}.dll");
        try
        {
            var rawDll = File.ReadAllBytes(systemDll);
            var sig = Ed25519.Sign(rawDll, Ed25519.DefaultDevKeySeed);
            byte[] signedDll = [..rawDll, ..sig];
            File.WriteAllBytes(tempDll, signedDll);

            var ptr = CpuKernelLoader.LoadAotKernelPtr("GetTickCount", tempDll);
            Assert.NotEqual(IntPtr.Zero, ptr);

            var del = CpuKernelLoader.LoadAotKernel<GetTickCountDelegate>("GetTickCount", tempDll);
            Assert.NotNull(del);
            _ = del();

            var ptrAgain = CpuKernelLoader.LoadAotKernelPtr("GetTickCount", tempDll);
            Assert.Equal(ptr, ptrAgain);
        }
        finally
        {
            CpuKernelLoader.Shutdown();
            if (File.Exists(tempDll))
            {
                try
                {
                    File.Delete(tempDll);
                }
                catch
                {
                    // Ignore if OS handle is still releasing
                }
            }
        }
    }
}
