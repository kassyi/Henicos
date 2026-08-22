@echo off
setlocal

echo Setting up Visual Studio C++ Environment...
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo Error: vswhere.exe not found. Please install Visual Studio.
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do (
    set "VS_PATH=%%i"
)

if not defined VS_PATH (
    echo Error: Visual Studio C++ compiler not found.
    exit /b 1
)

set "VCVARS_PATH=%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat"
if not exist "%VCVARS_PATH%" (
    echo Error: vcvars64.bat not found at %VCVARS_PATH%
    exit /b 1
)

call "%VCVARS_PATH%"

set "NVCC_CMD=nvcc.exe"
if defined CUDA_PATH (
    set "NVCC_CMD=%CUDA_PATH%\bin\nvcc.exe"
) else (
    if exist "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe" (
        set "NVCC_CMD=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe"
    ) else (
        echo Warning: CUDA_PATH is not set. Will try using 'nvcc' from PATH.
    )
)

pushd "%~dp0"

echo Building Kassyi.Math.GpuBigInt.Cuda.dll...
"%NVCC_CMD%" -shared -o "%~dp0Kassyi.Math.GpuBigInt.Cuda.dll" GpuBigInt.cu -lcufft -O3 -arch=sm_86 -std=c++17 -Xcompiler "/wd 4819"

if %errorlevel% neq 0 (
    echo Build Failed!
    popd
    exit /b %errorlevel%
)

echo Build Succeeded: %~dp0Kassyi.Math.GpuBigInt.Cuda.dll
popd
endlocal
