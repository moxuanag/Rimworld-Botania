<#
  部署脚本：把「游戏需要的东西」从源码仓库同步到 Mods\[GZ]Botania。

  背景：Steam 创意工坊上传时会**上传模组根目录的全部内容**（引擎里
  GetWorkshopUploadDirectory() 直接返回 RootDir，没有任何过滤）。
  所以 Mods 里只能放游戏文件，源码/文档/版本库留在 _dev\Botania-src。

  用法：
    pwsh -ExecutionPolicy Bypass -File deploy.ps1            # 先编译再部署
    pwsh -ExecutionPolicy Bypass -File deploy.ps1 -NoBuild   # 只同步（已有 DLL）
#>
param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$srcRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $srcRoot)) 'Mods\[GZ]Botania'

Write-Host "源码目录: $srcRoot"
Write-Host "部署目标: $modRoot"

if (-not $NoBuild) {
    Write-Host "`n[1/3] 编译..." -ForegroundColor Cyan
    Push-Location (Join-Path $srcRoot 'Source')
    try {
        dotnet build -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }
    } finally {
        Pop-Location
    }
} else {
    Write-Host "`n[1/3] 跳过编译" -ForegroundColor Yellow
}

$dll = Join-Path $srcRoot 'Assemblies\Botania.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "找不到 $dll，先编译" }

Write-Host "`n[2/3] 清空部署目录..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $modRoot) {
    Get-ChildItem -LiteralPath $modRoot -Force | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }
} else {
    New-Item -ItemType Directory -Path $modRoot -Force | Out-Null
}

Write-Host "`n[3/3] 同步发布载荷..." -ForegroundColor Cyan
# 只同步游戏需要的：目录 + LoadFolders.xml + LICENSE（MIT 要求随分发保留）
$dirs  = @('About', 'Assemblies', 'Defs', 'Languages', 'Textures', 'Patches')
$files = @('LoadFolders.xml', 'LICENSE')

foreach ($d in $dirs) {
    $from = Join-Path $srcRoot $d
    if (-not (Test-Path -LiteralPath $from)) { continue }
    Copy-Item -LiteralPath $from -Destination $modRoot -Recurse -Force
    Write-Host "  + $d"
}
foreach ($f in $files) {
    $from = Join-Path $srcRoot $f
    if (-not (Test-Path -LiteralPath $from)) { continue }
    Copy-Item -LiteralPath $from -Destination $modRoot -Force
    Write-Host "  + $f"
}

# 报告
$all = Get-ChildItem -LiteralPath $modRoot -Recurse -File
$size = ($all | Measure-Object Length -Sum).Sum
Write-Host ("`n部署完成：{0} 个文件，{1:N2} MB" -f $all.Count, ($size / 1MB)) -ForegroundColor Green
Write-Host "Mods 目录顶层：" -ForegroundColor Cyan
Get-ChildItem -LiteralPath $modRoot -Force | ForEach-Object { Write-Host ("  {0}" -f $_.Name) }
