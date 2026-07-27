param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

Write-Host "Updating package version to: $Version"

$vsix = "src\SsmsSqlFormatter\source.extension.vsixmanifest"
if (-not (Test-Path $vsix)) { Write-Error "VSIX manifest not found: $vsix"; exit 2 }

[xml]$doc = Get-Content $vsix
$ns = $doc.DocumentElement.NamespaceURI
$identity = $doc.SelectSingleNode("//d:Identity", @{d=$ns})
if ($identity -ne $null) {
    $identity.Version = $Version
    $doc.Save($vsix)
    Write-Host "Updated VSIX manifest version to $Version"
} else { Write-Error "Identity node not found in VSIX manifest"; exit 3 }

# Update AssemblyInfo
$asm = "src\SsmsSqlFormatter\Properties\AssemblyInfo.cs"
if (-not (Test-Path $asm)) { Write-Error "AssemblyInfo not found: $asm"; exit 4 }

$text = Get-Content $asm -Raw
$text = [System.Text.RegularExpressions.Regex]::Replace($text, 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(\"$Version\")")
$text = [System.Text.RegularExpressions.Regex]::Replace($text, 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(\"$Version\")")
Set-Content -Path $asm -Value $text -Encoding UTF8
Write-Host "Updated AssemblyInfo versions to $Version"

exit 0
