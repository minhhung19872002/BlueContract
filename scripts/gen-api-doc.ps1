<#
.SYNOPSIS
  Regenerates the endpoint table of docs/API.md from the running API's endpoint metadata (routes, permissions, types,
  XML comments). ApiDocTests fails in CI when the committed table is stale.
.EXAMPLE
  scripts\gen-api-doc.ps1   # needs Docker for the test containers, like the integration tests
#>
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$env:BLUEPROCURE_UPDATE_API_DOC = '1'
try {
    dotnet test backend/tests/BlueContract.Api.IntegrationTests --filter 'FullyQualifiedName~ApiDocTests' --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed' }
}
finally {
    Remove-Item Env:BLUEPROCURE_UPDATE_API_DOC -ErrorAction SilentlyContinue
}
Write-Host 'docs/API.md updated'
