$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$output = Join-Path $root 'dist'

Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'HonorPCHelper.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem $output -File | Where-Object Name -ne 'HonorPCHelper.exe' | Remove-Item -Force
Write-Host "Done: $output\HonorPCHelper.exe"
