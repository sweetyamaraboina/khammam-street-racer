param(
    [string]$NewVersion = "",
    [string]$ReleaseNotes = "Updated Khammam Street Racer build and assets.",
    [string]$Token = $env:GITHUB_TOKEN
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projDir = "C:\Users\sweet\.gemini\antigravity\scratch\khammam-street-racer"
$tokenFile = "$projDir\.gh_token"
$devFile = "$projDir\.gh_device.json"
$clientId = "178c6fc778ccc68e1d6a"

# 1. Load existing GitHub Token if present
if ([string]::IsNullOrWhiteSpace($Token) -and (Test-Path $tokenFile)) {
    $Token = (Get-Content $tokenFile -Raw).Trim()
}

# 2. Check pending device flow code if present
if ([string]::IsNullOrWhiteSpace($Token) -and (Test-Path $devFile)) {
    try {
        $dcResp = Get-Content $devFile -Raw | ConvertFrom-Json
        $testPoll = Invoke-RestMethod -Method Post -Uri "https://github.com/login/oauth/access_token" `
            -Headers @{ Accept = "application/json" } `
            -Body @{
                client_id   = $clientId
                device_code = $dcResp.device_code
                grant_type  = "urn:ietf:params:oauth:grant-type:device_code"
            }
        if ($testPoll.access_token) {
            $Token = $testPoll.access_token
            Set-Content -Path $tokenFile -Value $Token -NoNewline
            Remove-Item $devFile -Force -ErrorAction SilentlyContinue
            Write-Host "[OK] Authenticated with GitHub successfully!" -ForegroundColor Green
        }
        elseif ($testPoll.error) {
            Write-Host "[INFO] Device code status: $($testPoll.error)" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "[WARN] Could not poll existing device code: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "GitHub authorization is still pending or expired. Please complete authorization at https://github.com/login/device"
}

$headers = @{
    Authorization = "Bearer $Token"
    Accept        = "application/vnd.github+json"
    "User-Agent"  = "KhammamStreetRacer-Publisher"
}

# 3. Get authenticated GitHub username and ensure repo exists
$user = Invoke-RestMethod -Method Get -Uri "https://api.github.com/user" -Headers $headers
$owner = $user.login
$repoName = "khammam-street-racer"

Write-Host "[OK] Signed in as GitHub user: $owner" -ForegroundColor Cyan

try {
    $repo = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$owner/$repoName" -Headers $headers
    Write-Host "[OK] Found existing GitHub repository: $($repo.html_url)" -ForegroundColor Green
}
catch {
    Write-Host "[INFO] Creating public GitHub repository '$owner/$repoName'..." -ForegroundColor Yellow
    $createBody = @{
        name        = $repoName
        description = "Khammam Dwaraka Nagar Realistic Street View and Bike Race Desktop App with Auto-Updater"
        private     = $false
        auto_init   = $true
    } | ConvertTo-Json
    $repo = Invoke-RestMethod -Method Post -Uri "https://api.github.com/user/repos" -Headers $headers -Body $createBody -ContentType "application/json"
    Write-Host "[OK] Created repository: $($repo.html_url)" -ForegroundColor Green
    Start-Sleep -Seconds 3
}

# 4. Update github-config.json with owner and repo
$ghCfg = @{
    owner  = $owner
    repo   = $repoName
    branch = "main"
} | ConvertTo-Json
Set-Content -Path "$projDir\github-config.json" -Value $ghCfg -Encoding UTF8

# 5. Bump Version in version.json
$verObj = Get-Content "$projDir\version.json" -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($NewVersion)) {
    $parts = $verObj.version.Split('.')
    $patch = [int]$parts[2] + 1
    $NewVersion = "$($parts[0]).$($parts[1]).$patch"
}

$verJson = @{
    version     = $NewVersion
    publishedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    notes       = $ReleaseNotes
} | ConvertTo-Json
Set-Content -Path "$projDir\version.json" -Value $verJson -Encoding UTF8
Write-Host "[OK] Version set to v$NewVersion" -ForegroundColor Cyan

# 6. Rebuild executables with updated version and GitHub config
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$projDir\build.ps1"

# 7. Upload files to GitHub Repo via Contents API
function Push-GitHubFile([string]$LocalPath, [string]$RemotePath, [string]$CommitMsg) {
    Write-Host "[PUSH] Uploading $RemotePath to GitHub..." -ForegroundColor Cyan
    $bytes = [System.IO.File]::ReadAllBytes($LocalPath)
    $b64 = [Convert]::ToBase64String($bytes)
    $apiUrl = "https://api.github.com/repos/$owner/$repoName/contents/$RemotePath"

    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $sha = $null
            try {
                $existing = Invoke-RestMethod -Method Get -Uri $apiUrl -Headers $headers
                $sha = $existing.sha
            }
            catch { }

            $bodyHash = @{
                message = $CommitMsg
                content = $b64
                branch  = "main"
            }
            if ($sha) { $bodyHash.sha = $sha }

            $bodyJson = $bodyHash | ConvertTo-Json -Compress
            Invoke-RestMethod -Method Put -Uri $apiUrl -Headers $headers -Body $bodyJson -ContentType "application/json" | Out-Null
            break
        }
        catch {
            if ($attempt -eq 3) { throw $_ }
            Write-Host "[RETRY] Transient GitHub response, retrying $RemotePath (attempt $($attempt + 1)/3)..." -ForegroundColor Yellow
            Start-Sleep -Seconds 2
        }
    }
}

Push-GitHubFile "$projDir\github-config.json" "github-config.json" "Configure GitHub auto-updater repo ($owner/$repoName)"
Push-GitHubFile "$projDir\src\KhammamStreetRacer.cs" "src/KhammamStreetRacer.cs" "Update application source for v$NewVersion"
Push-GitHubFile "$projDir\src\KhammamSetup.cs" "src/KhammamSetup.cs" "Update installer source for v$NewVersion"
Push-GitHubFile "$projDir\build.ps1" "build.ps1" "Add build script"
Push-GitHubFile "$projDir\publish-update.ps1" "publish-update.ps1" "Add auto-publish script"
Push-GitHubFile "$projDir\dist\KhammamStreetRacer.exe" "dist/KhammamStreetRacer.exe" "Publish KhammamStreetRacer.exe v$NewVersion"
Push-GitHubFile "$projDir\version.json" "version.json" "Bump version to v$NewVersion"

Write-Host "[DONE] Successfully pushed v$NewVersion to https://github.com/$owner/$repoName !" -ForegroundColor Green
