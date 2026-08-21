@echo off
setlocal

echo Setting up Visual Studio C++ Environment...
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"

echo Building HenicosCuda.dll...
"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe" -shared -arch=native -o ..\HenicosCuda.dll kernel.cu -lcufft

if %errorlevel% neq 0 (
    echo Build Failed!
    exit /b %errorlevel%
)

echo Build Succeeded: ..\HenicosCuda.dll
endlocal
