using System;
using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using Xunit;
using Kassyi.GSharp;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests
{
    public class JitCompilerTests
    {
        [Fact]
        public void CompileAndLoad_ShouldCleanUpFiles_OnSuccess()
        {
            var mockFs = new MockFileSystem();
            mockFs.AddDirectory(mockFs.Path.GetTempPath());
            
            // Mock environment setup to avoid real execution
            mockFs.AddFile(@"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe", new MockFileData(""));
            mockFs.AddFile(@"C:\VC\Auxiliary\Build\vcvars64.bat", new MockFileData(""));
            mockFs.AddFile(@"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe", new MockFileData(""));

            NvccCompiler.FileSystem = mockFs;
            NativeModuleLoader.FileSystem = mockFs;
            JitCompiler.FileSystem = mockFs;
            
            Environment.SetEnvironmentVariable("ProgramFiles(x86)", @"C:\Program Files (x86)");
            
            NvccCompiler.GetVswhereOutputFunc = () => @"C:\VS";
            mockFs.AddFile(@"C:\VS\VC\Auxiliary\Build\vcvars64.bat", new MockFileData(""));
            
            // Mock process runner that fakes vswhere and nvcc
            NvccCompiler.ProcessRunner = psi =>
            {
                if (psi.FileName.Contains("vswhere.exe"))
                {
                    // This is just a fake, we intercepted vswhere manually in GetVcvarsPath
                }
                else if (psi.FileName == "cmd.exe")
                {
                    // Mock nvcc compilation: just create the dll file
                    var files = mockFs.Directory.GetFiles(mockFs.Path.Combine(mockFs.Path.GetTempPath(), "KassyiGSharpJit"));
                    foreach(var f in files)
                    {
                        if (f.EndsWith(".txt"))
                        {
                            var content = mockFs.File.ReadAllText(f);
                            int oIndex = content.IndexOf("-o\r\n") + 4;
                            if (oIndex < 4) oIndex = content.IndexOf("-o\n") + 3;
                            int endIdx = content.IndexOf("\n", oIndex);
                            string dllPath = content.Substring(oIndex, endIdx - oIndex).Trim();
                            mockFs.AddFile(dllPath, new MockFileData("dummy dll content"));
                        }
                    }
                }
            };
            
            // Mock NativeModuleLoader
            NativeModuleLoader.LoadLibraryFunc = path => new IntPtr(1234);
            NativeModuleLoader.GetExportFunc = (h, name) => new IntPtr(5678);
            NativeModuleLoader.FreeLibraryFunc = h => { };

            using (var kernel = JitCompiler.CompileAndLoad<Action>("__global__ void test() {}", "test_func"))
            {
                Assert.NotNull(kernel);
                
                // .cu file should be deleted even on success
                var files = mockFs.Directory.GetFiles(mockFs.Path.Combine(mockFs.Path.GetTempPath(), "KassyiGSharpJit"));
                Assert.DoesNotContain(files, f => f.EndsWith(".cu"));
                
                // options .txt and .bat should be deleted
                Assert.DoesNotContain(files, f => f.EndsWith(".txt"));
                Assert.DoesNotContain(files, f => f.EndsWith(".bat"));
                
                // .dll should still exist while loaded
                Assert.Contains(files, f => f.EndsWith(".dll"));
            }
            
            // After dispose, dll should be deleted
            var afterFiles = mockFs.Directory.GetFiles(mockFs.Path.Combine(mockFs.Path.GetTempPath(), "KassyiGSharpJit"));
            Assert.DoesNotContain(afterFiles, f => f.EndsWith(".dll"));
        }

        [Fact]
        public void CompileAndLoad_ShouldCleanUpFiles_OnCompilationFailure()
        {
            var mockFs = new MockFileSystem();
            mockFs.AddDirectory(mockFs.Path.GetTempPath());
            
            mockFs.AddFile(@"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe", new MockFileData(""));
            mockFs.AddFile(@"C:\VC\Auxiliary\Build\vcvars64.bat", new MockFileData(""));
            mockFs.AddFile(@"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe", new MockFileData(""));

            NvccCompiler.FileSystem = mockFs;
            NativeModuleLoader.FileSystem = mockFs;
            JitCompiler.FileSystem = mockFs;
            
            Environment.SetEnvironmentVariable("ProgramFiles(x86)", @"C:\Program Files (x86)");
            
            NvccCompiler.GetVswhereOutputFunc = () => @"C:\VS";
            mockFs.AddFile(@"C:\VS\VC\Auxiliary\Build\vcvars64.bat", new MockFileData(""));
            
            // Mock process runner that fakes compilation failure (does not create DLL)
            NvccCompiler.ProcessRunner = psi =>
            {
                if (psi.FileName == "cmd.exe")
                {
                    throw new Exception("Process failed"); // Simulate compilation error
                }
            };
            
            Assert.Throws<Exception>(() => 
                JitCompiler.CompileAndLoad<Action>("__global__ void test() {}", "test_func")
            );
            
            // Verify cleanup on failure
            var files = mockFs.Directory.GetFiles(mockFs.Path.Combine(mockFs.Path.GetTempPath(), "KassyiGSharpJit"));
            Assert.DoesNotContain(files, f => f.EndsWith(".cu"));
            Assert.DoesNotContain(files, f => f.EndsWith(".txt"));
            Assert.DoesNotContain(files, f => f.EndsWith(".bat"));
            Assert.DoesNotContain(files, f => f.EndsWith(".dll"));
        }
    }
}
