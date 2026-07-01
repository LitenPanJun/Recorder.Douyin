<#
.SYNOPSIS
    构建 Recorder.Douyin 多架构发布包
.DESCRIPTION
    为 win-x64 / win-arm64 / linux-x64 / linux-arm64 分别构建：
      - fd       框架依赖单文件（需要 .NET 10）
      - fd-split 框架依赖分离（需要 .NET 10）
      - sc       自包含单文件（无需 .NET 10）
      - sc-split 自包含分离（无需 .NET 10）

    Windows → .zip,  Linux → .tar.gz
#>
param(
    [string]$Version = "0.1.0",
    [string[]]$Rids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64")
)

$ErrorActionPreference = "Stop"
$Project = "Recorder.Core\Recorder.Core.csproj"
$DistRoot = Join-Path (Get-Location) "dist"

function New-Directory { param($Path) if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null } }

$CommonArgs = @(
    "-c", "Release",
    "-p:DebugType=none",
    "-p:DebugSymbols=false"
)

# 清理
if (Test-Path $DistRoot) { Remove-Item $DistRoot -Recurse -Force }
New-Directory $DistRoot

$Configs = @(
    @{ Name = "fd";       SelfContained = $false; SingleFile = $true  }
    @{ Name = "fd-split"; SelfContained = $false; SingleFile = $false }
    @{ Name = "sc";       SelfContained = $true;  SingleFile = $true  }
    @{ Name = "sc-split"; SelfContained = $true;  SingleFile = $false }
)

foreach ($rid in $Rids)
{
    $isWin = $rid -like "win-*"
    Write-Host "`n========== $rid ==========" -ForegroundColor Cyan

    foreach ($cfg in $Configs)
    {
        $name = $cfg.Name
        $outDir = Join-Path $DistRoot "Recorder.Douyin-v$Version-$rid-$name"
        New-Directory $outDir

        Write-Host "[$name] 构建中..." -ForegroundColor Yellow
        $publishArgs = $CommonArgs + @(
            "-r", $rid
            "--self-contained", $cfg.SelfContained.ToString().ToLower()
            "-p:PublishSingleFile=$($cfg.SingleFile.ToString().ToLower())"
        )
        if ($cfg.SelfContained -and $cfg.SingleFile)
        {
            $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
            $publishArgs += "-p:EnableCompressionInSingleFile=true"
        }
        elseif ($cfg.SingleFile)
        {
            $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
        }
        $publishArgs += @("-o", $outDir)

        & dotnet publish $Project @publishArgs 2>&1 | ForEach-Object { "$_" }

        # 清理残留 pdb
        Get-ChildItem $outDir -Recurse -Filter *.pdb | Remove-Item -Force

        # 打包
        $baseName = "Recorder.Douyin-v$Version-$rid-$name"
        if ($isWin)
        {
            $zipPath = Join-Path $DistRoot "$baseName.zip"
            Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
            Write-Host "      -> $zipPath" -ForegroundColor Green
        }
        else
        {
            $tarball = Join-Path $DistRoot "$baseName.tar.gz"
            $srcDir = "Recorder.Douyin-v$Version-$rid-$name"
            $tmpTar = [System.IO.Path]::GetTempFileName()
            Remove-Item $tmpTar -Force
            Push-Location $DistRoot
            & tar -czf $tmpTar $srcDir 2>&1 | ForEach-Object { "$_" }
            Pop-Location
            Move-Item $tmpTar $tarball -Force
            Write-Host "      -> $tarball" -ForegroundColor Green
        }

        Remove-Item $outDir -Recurse -Force
    }
}

Write-Host "`n========== 全部完成 ==========" -ForegroundColor Cyan
Get-ChildItem $DistRoot | Sort-Object Name | Select-Object Name, @{N="Size";E={
    if ($_.Extension -eq ".zip") { "{0:N0} KB" -f ($_.Length / 1KB) }
    else { "{0:N0} KB" -f ($_.Length / 1KB) }
}} | Format-Table -AutoSize
