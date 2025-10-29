# PowerShell script to push Android Recovery Tool to GitHub
# Run this script after creating a repository on GitHub

Write-Host "Android File Recovery Tool - Git Push Script" -ForegroundColor Cyan
Write-Host "============================================`n" -ForegroundColor Cyan

$githubUsername = Read-Host "Enter your GitHub username"
$repoName = Read-Host "Enter repository name (default: android-file-recovery-tool)" 

if ([string]::IsNullOrWhiteSpace($repoName)) {
    $repoName = "android-file-recovery-tool"
}

$remoteUrl = "https://github.com/$githubUsername/$repoName.git"

Write-Host "`nSetting up remote repository..." -ForegroundColor Yellow
Write-Host "Remote URL: $remoteUrl`n" -ForegroundColor Gray

# Check if remote already exists
$existingRemote = git remote get-url origin 2>$null
if ($existingRemote) {
    Write-Host "Remote 'origin' already exists: $existingRemote" -ForegroundColor Yellow
    $overwrite = Read-Host "Overwrite? (y/N)"
    if ($overwrite -eq "y" -or $overwrite -eq "Y") {
        git remote set-url origin $remoteUrl
        Write-Host "Remote URL updated." -ForegroundColor Green
    } else {
        Write-Host "Keeping existing remote." -ForegroundColor Yellow
        $remoteUrl = $existingRemote
    }
} else {
    git remote add origin $remoteUrl
    Write-Host "Remote 'origin' added." -ForegroundColor Green
}

Write-Host "`nPushing to GitHub..." -ForegroundColor Yellow
git push -u origin main

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ Successfully pushed to GitHub!" -ForegroundColor Green
    Write-Host "Repository URL: https://github.com/$githubUsername/$repoName" -ForegroundColor Cyan
} else {
    Write-Host "`n❌ Push failed. Please check:" -ForegroundColor Red
    Write-Host "1. Repository exists on GitHub" -ForegroundColor Yellow
    Write-Host "2. You have push access" -ForegroundColor Yellow
    Write-Host "3. You're authenticated (username/password or token)" -ForegroundColor Yellow
    Write-Host "`nYou may need to: git push -u origin main" -ForegroundColor Yellow
}

