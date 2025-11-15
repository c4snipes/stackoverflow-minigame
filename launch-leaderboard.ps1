#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir
$previousMode = $env:STACKOVERFLOW_MINIGAME_MODE
$env:STACKOVERFLOW_MINIGAME_MODE = 'leaderboard'
try {
    dotnet run
}
finally {
    if ($null -eq $previousMode) {
        Remove-Item Env:STACKOVERFLOW_MINIGAME_MODE -ErrorAction SilentlyContinue
    }
    else {
        $env:STACKOVERFLOW_MINIGAME_MODE = $previousMode
    }
}
