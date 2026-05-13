param(
    [string]$ProjectPath = "Mdfproject",
    [string]$UnityExe = "",
    [string]$UnityVersion = "2021.3.45f1",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if (-not [System.IO.Path]::IsPathRooted($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot $ProjectPath
}
$ProjectPath = (Resolve-Path $ProjectPath).Path

if ([string]::IsNullOrWhiteSpace($UnityExe)) {
    $candidates = @(
        $env:UNITY_EDITOR_PATH,
        "E:\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe",
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe",
        "C:\Program Files\Unity\Editor\Unity.exe"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            $UnityExe = (Resolve-Path $candidate).Path
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($UnityExe) -or -not (Test-Path -LiteralPath $UnityExe)) {
    throw "Unity.exe was not found. Pass -UnityExe with the full Unity editor path."
}

$sensitiveNamePattern = '(?i)(api[_-]?key|secret|token|password|passwd|credential|account[_-]?no|kis[_-]?real|openrouter|gemini|deepseek|posthog|anthropic|openai)'
$namesToScrub = Get-ChildItem Env: |
    Where-Object { $_.Name -match $sensitiveNamePattern } |
    Select-Object -ExpandProperty Name |
    Sort-Object -Unique

Write-Host "Unity executable: $UnityExe"
Write-Host "Project path: $ProjectPath"

if ($namesToScrub.Count -gt 0) {
    Write-Host "Scrubbing environment variable names for this Unity process:"
    foreach ($name in $namesToScrub) {
        Write-Host "  $name"
    }
} else {
    Write-Host "No matching sensitive environment variables found in this shell."
}

if ($DryRun) {
    Write-Host "Dry run only. Unity was not started."
    exit 0
}

foreach ($name in $namesToScrub) {
    Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
}

Start-Process -FilePath $UnityExe -ArgumentList @("-projectPath", $ProjectPath) -WorkingDirectory $repoRoot
Write-Host "Started Unity with the scrubbed process environment."
