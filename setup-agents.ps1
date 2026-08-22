#!/usr/bin/env pwsh
# Setup Antigravity Global Agents

Write-Host "Setting up Antigravity Global Agents..." -ForegroundColor Cyan
npx --yes "git+ssh://git@github.com/kassyi/antigravity-global-agents.git" --copy

if ($LASTEXITCODE -eq 0) {
    Write-Host "Setup completed successfully." -ForegroundColor Green
} else {
    Write-Host "Setup failed." -ForegroundColor Red
}
