# Packs the sample plugins into a local NuGet feed, so the demo's Browse page has something to show.
#   ./samples/pack-samples.ps1
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\Pluginz.Demo\feed'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $Output | Out-Null

foreach ($project in 'Pluginz.Samples.Contracts', 'Pluginz.Samples.Hello', 'Pluginz.Samples.Web') {
    dotnet pack (Join-Path $PSScriptRoot "$project\$project.csproj") -c $Configuration -o $Output --nologo
}

Write-Host "Packed into $((Resolve-Path $Output).Path)"
