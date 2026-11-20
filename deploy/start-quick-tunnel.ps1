# Quick Tunnel for Aqarak Online - quick demo only.
# Usage: start the app first on http://localhost:5000, then run this script
# from PowerShell (right-click -> Run with PowerShell). It prints a temporary
# trycloudflare.com link that changes every time and stops when you close the window.
# Requires: cloudflared installed and in PATH (download from Cloudflare official site).

$ErrorActionPreference = "Stop"

$cloudflared = Get-Command cloudflared -ErrorAction SilentlyContinue
if (-not $cloudflared) {
    Write-Host "cloudflared not found in PATH. Download it from Cloudflare official site, then re-run this script." -ForegroundColor Red
    exit 1
}

try {
    $response = Invoke-WebRequest -Uri "http://localhost:5000" -UseBasicParsing -TimeoutSec 5
    Write-Host "App is running on http://localhost:5000 (Status: $($response.StatusCode))." -ForegroundColor Green
} catch {
    Write-Host "App is not responding on http://localhost:5000. Start it first, then re-run this script." -ForegroundColor Red
    exit 1
}

Write-Host "Starting Quick Tunnel... copy the trycloudflare.com link below." -ForegroundColor Cyan
cloudflared tunnel --url http://localhost:5000
