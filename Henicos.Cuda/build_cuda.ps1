# Henicos.Cuda 構築用スクリプト
$ErrorActionPreference = "Stop"

$nvcc = "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe"
$outputDll = "..\HenicosCuda.dll"

Write-Host "Building HenicosCuda.dll..."
# -shared フラグで DLL (Shared Library) を作成
# cuFFT をリンクする場合は -lcufft を追加する
& $nvcc -shared -o $outputDll kernel.cu -lcufft

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build Succeeded: $outputDll" -ForegroundColor Green
} else {
    Write-Host "Build Failed!" -ForegroundColor Red
}
