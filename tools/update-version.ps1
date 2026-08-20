param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

Write-Host "Updating package version to: $Version"

$vsix = "src\SsmsSqlFormatter\source.extension.vsixmanifest"
if (-not (Test-Path $vsix)) { Write-Error "VSIX manifest not found: $vsix"; exit 2 }

[xml]$doc = Get-Content $vsix
$nsUri = $doc.DocumentElement.NamespaceURI
$nsMgr = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$nsMgr.AddNamespace("d", $nsUri)
$identity = $doc.SelectSingleNode("//d:Identity", $nsMgr)
if ($identity -ne $null) {
    $identity.Version = $Version
    $doc.Save($vsix)
    Write-Host "Updated VSIX manifest version to $Version"
} else { Write-Error "Identity node not found in VSIX manifest"; exit 3 }

# Update AssemblyInfo
$asm = "src\SsmsSqlFormatter\Properties\AssemblyInfo.cs"
if (-not (Test-Path $asm)) { Write-Error "AssemblyInfo not found: $asm"; exit 4 }

$text = Get-Content $asm -Raw
$text = [System.Text.RegularExpressions.Regex]::Replace($text, 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$Version`")")
$text = [System.Text.RegularExpressions.Regex]::Replace($text, 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$Version`")")
Set-Content -Path $asm -Value $text -Encoding UTF8
Write-Host "Updated AssemblyInfo versions to $Version"

# Keep the User Guide's cover/footer version+date in sync, then regenerate its PDF -
# see docs\SSMSSQLFormatter-User-Guide.html's own comment on SSF_GUIDE_VERSION for why
# this is the single source of truth rather than hand-editing the cover/footer text.
$guide = "docs\SSMSSQLFormatter-User-Guide.html"
if (Test-Path $guide) {
    $today = Get-Date -Format "yyyy-MM-dd"
    $guideText = Get-Content $guide -Raw
    $guideText = [System.Text.RegularExpressions.Regex]::Replace(
        $guideText,
        "var SSF_GUIDE_VERSION = \{ version: '[^']+', updated: '[^']+' \};",
        "var SSF_GUIDE_VERSION = { version: '$Version', updated: '$today' };")
    Set-Content -Path $guide -Value $guideText -Encoding UTF8
    Write-Host "Updated User Guide version to $Version ($today)"

    $pdfScript = Join-Path $PSScriptRoot "generate-user-guide-pdf.ps1"
    if (Test-Path $pdfScript) {
        & $pdfScript
    } else {
        Write-Warning "generate-user-guide-pdf.ps1 not found - regenerate the PDF manually."
    }
} else {
    Write-Host "No User Guide found at $guide - skipping."
}

exit 0
