param(
    [string]$Configuration = "Release"
)

Write-Host "Build script starting (Configuration=$Configuration)"

function Find-VsWhere {
    $candidates = @("${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe", "$env:ProgramFiles\Microsoft Visual Studio\Installer\vswhere.exe")
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

# Restore NuGet packages
Write-Host "Restoring NuGet packages..."
nuget restore SsmsSqlFormatter.sln
if ($LASTEXITCODE -ne 0) { Write-Error "nuget restore failed"; exit $LASTEXITCODE }

# Find msbuild
Write-Host "Locating MSBuild..."
$msbuildCmd = Get-Command msbuild -ErrorAction SilentlyContinue
if ($msbuildCmd) {
    $msbuild = $msbuildCmd.Path
} else {
    $vswhere = Find-VsWhere
    if (-not $vswhere) { Write-Error "vswhere.exe not found and msbuild not on PATH"; exit 2 }
    $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if (-not $installPath) { Write-Error "Could not find Visual Studio installation via vswhere"; exit 3 }
    $candidate = Join-Path $installPath "MSBuild\Current\Bin\MSBuild.exe"
    if (-not (Test-Path $candidate)) {
        $candidate = Join-Path $installPath "Team Tools\Performance Tools\MSBuild.exe"
    }
    if (-not (Test-Path $candidate)) { Write-Error "MSBuild.exe not found under Visual Studio install"; exit 4 }
    $msbuild = $candidate
}

Write-Host "Using MSBuild: $msbuild"

Write-Host "Building solution..."
& $msbuild "SsmsSqlFormatter.sln" /p:Configuration=$Configuration /m
if ($LASTEXITCODE -ne 0) { Write-Error "MSBuild failed"; exit $LASTEXITCODE }

# Locate vstest.console.exe to run tests
Write-Host "Locating vstest.console.exe..."
$vswhere = Find-VsWhere
if (-not $vswhere) { Write-Error "vswhere.exe not found"; exit 5 }
$installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $installPath) { Write-Error "Could not find Visual Studio installation via vswhere"; exit 6 }
$vstest = Join-Path $installPath 'Common7\IDE\Extensions\TestPlatform\vstest.console.exe'
if (-not (Test-Path $vstest)) { Write-Error "vstest.console.exe not found at $vstest"; exit 7 }

$testDll = "src\SsmsSqlFormatter.Tests\bin\$Configuration\SsmsSqlFormatter.Tests.dll"
if (-not (Test-Path $testDll)) { Write-Error "Test assembly not found: $testDll"; exit 8 }

Write-Host "Running tests: $testDll"
& $vstest $testDll
if ($LASTEXITCODE -ne 0) { Write-Error "Tests failed"; exit $LASTEXITCODE }

Write-Host "Build and tests succeeded."
exit 0