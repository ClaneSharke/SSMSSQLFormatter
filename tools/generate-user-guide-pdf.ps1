# Regenerates docs\SSMSSQLFormatter-User-Guide.pdf from docs\SSMSSQLFormatter-User-Guide.html
# via headless Chrome/Edge's print-to-PDF - the same rendering engine that produces the
# @media print layout when a user opens the guide and prints it themselves. Run this after
# any change to the guide's content (and whenever tools\update-version.ps1 bumps the version,
# since the guide's cover/footer version and date should stay in sync with the PDF).

$repoRoot = Split-Path -Parent $PSScriptRoot
$html = Join-Path $repoRoot "docs\SSMSSQLFormatter-User-Guide.html"
$pdf = Join-Path $repoRoot "docs\SSMSSQLFormatter-User-Guide.pdf"

if (-not (Test-Path $html)) { Write-Error "Guide not found: $html"; exit 1 }

$browserCandidates = @(
    "${env:ProgramFiles}\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles}\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
)
$browser = $browserCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $browser) {
    Write-Error "Could not find Chrome or Edge. Install one, or pass its path as -BrowserPath."
    exit 2
}

$fileUrl = "file:///" + ($html -replace '\\', '/')
Write-Host "Using: $browser"
Write-Host "Rendering $html -> $pdf"
& $browser --headless --disable-gpu --no-pdf-header-footer --print-to-pdf="$pdf" "$fileUrl"

if (Test-Path $pdf) {
    Write-Host "Done. $((Get-Item $pdf).Length) bytes written to $pdf"
} else {
    Write-Error "PDF was not created."
    exit 3
}
