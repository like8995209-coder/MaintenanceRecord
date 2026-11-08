$ErrorActionPreference = 'Stop'
$appVersion = '6.6.6'

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw '未找到 .NET SDK。请先安装 .NET 8 SDK。'
}
$dotnet = $dotnetCommand.Source

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if ($null -eq $iscc) {
    throw '未找到 Inno Setup 6 的 ISCC.exe。请先安装 Inno Setup 6。'
}
$project = Join-Path $PSScriptRoot 'MaintenanceRecordApp\MaintenanceRecordApp.csproj'
$tests = Join-Path $PSScriptRoot 'MaintenanceRecordTests\MaintenanceRecordTests.csproj'
$publishDir = Join-Path $PSScriptRoot 'MaintenanceRecordApp\Publish\win-x64'
$installerSource = Join-Path $PSScriptRoot 'Installer\MaintenanceRecord.iss'
$outputDir = Join-Path $PSScriptRoot 'Builds'
$installerOutput = Join-Path $outputDir "MaintenanceRecord-Setup-$appVersion.exe"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& $dotnet test $tests -c Release --logger 'console;verbosity=minimal'

$publishRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot 'MaintenanceRecordApp\Publish'))
$resolvedPublishDir = [System.IO.Path]::GetFullPath($publishDir)
if (-not $resolvedPublishDir.StartsWith(
        $publishRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录超出预期范围：$resolvedPublishDir"
}
if (Test-Path -LiteralPath $resolvedPublishDir) {
    Remove-Item -LiteralPath $resolvedPublishDir -Recurse -Force
}

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $publishDir

$optionalPdfAssemblies = @(
    'PdfSharp.Charting.dll',
    'PdfSharp.Quality.dll',
    'PdfSharp.Snippets.dll'
)
foreach ($assembly in $optionalPdfAssemblies) {
    $path = Join-Path $resolvedPublishDir $assembly
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

# 本程序只使用 WPF，不依赖随 WindowsDesktop 运行时附带的无用组件。
# 清单已在裁剪副本中逐页启动验证，并保留中文 WPF 资源。
$optionalRuntimeFiles = @(
    'createdump.exe',
    'dbgshim.dll',
    'mscordaccore.dll',
    'mscordbi.dll',
    'clretwrc.dll',
    'Microsoft.DiaSymReader.Native.amd64.dll',
    'System.Diagnostics.EventLog.Messages.dll',
    'msquic.dll',
    'System.Net.Quic.dll',
    'System.DirectoryServices.dll',
    'System.Reflection.Metadata.dll',
    'System.Windows.Controls.Ribbon.dll',
    'System.Windows.Forms.dll',
    'System.Windows.Forms.Primitives.dll',
    'System.Windows.Forms.Design.dll',
    'System.Windows.Forms.Design.Editors.dll',
    'WindowsFormsIntegration.dll',
    'Microsoft.VisualBasic.dll',
    'Microsoft.VisualBasic.Core.dll',
    'Microsoft.VisualBasic.Forms.dll',
    'System.Design.dll',
    'System.Drawing.Design.dll'
)
foreach ($file in $optionalRuntimeFiles) {
    $path = Join-Path $resolvedPublishDir $file
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}
Get-ChildItem -LiteralPath $resolvedPublishDir -File -Filter 'mscordaccore_amd64_*.dll' |
    Remove-Item -Force

$optionalLocalizedFiles = @(
    'System.Windows.Forms.resources.dll',
    'System.Windows.Forms.Primitives.resources.dll',
    'System.Windows.Forms.Design.resources.dll',
    'WindowsFormsIntegration.resources.dll',
    'Microsoft.VisualBasic.Forms.resources.dll',
    'System.Windows.Controls.Ribbon.resources.dll',
    'System.Design.resources.dll',
    'System.Drawing.Design.resources.dll'
)
$localizedDirectory = Join-Path $resolvedPublishDir 'zh-Hans'
if (Test-Path -LiteralPath $localizedDirectory) {
    foreach ($file in $optionalLocalizedFiles) {
        $path = Join-Path $localizedDirectory $file
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

$requiredFiles = @(
    'MaintenanceRecord.exe',
    'MaintenanceRecord.dll',
    'PresentationFramework.dll',
    'PresentationCore.dll',
    'WindowsBase.dll',
    'e_sqlite3.dll',
    'ClosedXML.dll',
    'DocumentFormat.OpenXml.dll',
    'PdfSharp.dll'
)
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedPublishDir $file))) {
        throw "精简后缺少必要文件：$file"
    }
}

$applicationVersion = (Get-Item -LiteralPath (
    Join-Path $resolvedPublishDir 'MaintenanceRecord.exe')).VersionInfo.ProductVersion.Trim()
if ($applicationVersion -ne $appVersion) {
    throw "程序版本不一致：$applicationVersion"
}

Push-Location (Split-Path -Parent $installerSource)
try {
    & $iscc '/Qp' (Split-Path -Leaf $installerSource)
}
finally {
    Pop-Location
}

$installer = Get-Item -LiteralPath $installerOutput
$installerVersion = $installer.VersionInfo.ProductVersion.Trim()
if ($installerVersion -ne $appVersion) {
    throw "安装包版本不一致：$installerVersion"
}
$installer | Select-Object FullName, Length, LastWriteTime

$publishedFiles = Get-ChildItem -LiteralPath $resolvedPublishDir -Recurse -File
[pscustomobject]@{
    Version = $appVersion
    PublishedFiles = $publishedFiles.Count
    PublishedSizeMB = [math]::Round(
        (($publishedFiles | Measure-Object Length -Sum).Sum) / 1MB,
        2)
} | Format-List
