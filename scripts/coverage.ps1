<#
.SYNOPSIS
  Merges the Cobertura reports of all backend test projects and checks line coverage of Domain + Application (spec §13: >= 70 %).
.EXAMPLE
  cd backend; dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults; cd ..; scripts\coverage.ps1
#>
param([string]$ResultsDir = 'backend/TestResults', [double]$Min = 70)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$hits = @{}
foreach ($file in Get-ChildItem $ResultsDir -Recurse -Filter coverage.cobertura.xml) {
    [xml]$xml = Get-Content $file.FullName
    foreach ($pkg in $xml.coverage.packages.package) {
        if ($pkg.name -notin @('BlueContract.Domain', 'BlueContract.Application')) { continue }
        foreach ($cls in $pkg.classes.class) {
            foreach ($line in $cls.lines.line) {
                $key = "$($pkg.name)|$($cls.filename)|$($line.number)"
                if (-not $hits.ContainsKey($key) -or $hits[$key] -lt [int]$line.hits) { $hits[$key] = [int]$line.hits }
            }
        }
    }
}
if ($hits.Count -eq 0) { throw 'Không tìm thấy báo cáo coverage' }
foreach ($name in 'BlueContract.Domain', 'BlueContract.Application') {
    $lines = @($hits.Keys | Where-Object { $_.StartsWith("$name|") })
    $covered = @($lines | Where-Object { $hits[$_] -gt 0 }).Count
    Write-Host ('{0}: {1:N1}% ({2} dòng)' -f $name, (100 * $covered / $lines.Count), $lines.Count)
}
$total = 100 * @($hits.Values | Where-Object { $_ -gt 0 }).Count / $hits.Count
Write-Host ('Domain + Application: {0:N1}% (ngưỡng {1}%)' -f $total, $Min)
if ($total -lt $Min) { throw 'Coverage dưới ngưỡng' }
