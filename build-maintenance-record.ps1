$ErrorActionPreference = 'Stop'

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
$installerOutput = Join-Path $outputDir 'MaintenanceRecord-Setup-4.1.0.exe'

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

Push-Location (Split-Path -Parent $installerSource)
try {
    & $iscc '/Qp' (Split-Path -Leaf $installerSource)
}
finally {
    Pop-Location
}

Get-Item -LiteralPath $installerOutput |
    Select-Object FullName, Length, LastWriteTime
