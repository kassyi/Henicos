@echo off
setlocal

echo Setting up Visual Studio C++ Environment...
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do (
    set "VS_PATH=%%i"
)
set "VCVARS_PATH=%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat"
call "%VCVARS_PATH%"

set "NVCC_CMD=nvcc.exe"
if exist "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe" (
    set "NVCC_CMD=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe"
)

pushd "%~dp0"

echo Compiling benchmark...
"%NVCC_CMD%" bench_main.cu -o bench.exe -O3 -arch=sm_86 -std=c++17 -Xcompiler "/wd 4819 /Zc:preprocessor"

if %errorlevel% neq 0 (
    echo Build Failed!
    popd
    exit /b %errorlevel%
)

echo Running benchmark...
bench.exe

popd
endlocal
