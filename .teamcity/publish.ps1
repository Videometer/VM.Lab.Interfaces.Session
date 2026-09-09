# Pack and push one NuGet package to GitHub Packages.
# Carries the on-prem publish template's controls: the DLL version check (catches
# failed version stamping, rejects 0.0.0) and the nupkg archive copy to the S-drive.
# Deliberate change from on-prem: an already-published version SKIPS instead of
# failing, because with bump-as-release-signal most main merges do not bump.
param(
    [Parameter(Mandatory)][string] $PackageId,
    [string] $Project = '',   # csproj mode: dotnet pack this project
    [string] $Nuspec = '',    # nuspec mode: build the solution, nuget pack this nuspec
    [string] $Sln = ''        # solution to build in nuspec mode
)
$ErrorActionPreference = 'Stop'

$cl = 'src/ChangeLog.txt'
if (-not (Test-Path $cl)) { $cl = (Get-ChildItem -Recurse -Filter 'ChangeLog.txt' | Select-Object -First 1).FullName }
$top = Get-Content $cl | Where-Object { $_ -match '^\s*\d+(\.\d+)+\s*$' } | Select-Object -First 1
if (-not $top) { throw "No version found at the top of $cl" }
$v = $top.Trim()
Write-Host "$PackageId version from ChangeLog: $v"

$auth = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("oauth2:$($env:VM_FEED_TOKEN)"))
$versions = @()
try { $versions = (Invoke-RestMethod "https://nuget.pkg.github.com/Videometer/download/$($PackageId.ToLower())/index.json" -Headers @{ Authorization = $auth }).versions } catch {}
if ($versions -contains $v) { Write-Host "$v is already on the feed - nothing to publish"; exit 0 }

New-Item out -ItemType Directory -Force | Out-Null
if ($Nuspec) {
    dotnet build $Sln -c Release -p:Version=$v
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    nuget pack $Nuspec -Version $v -BasePath (Split-Path $Nuspec) -OutputDirectory out
    if ($LASTEXITCODE -ne 0) { throw 'pack failed' }
} else {
    dotnet build $Project -c Release -p:Version=$v
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    dotnet pack $Project -c Release -p:Version=$v --no-build -o out
    if ($LASTEXITCODE -ne 0) { throw 'pack failed' }
}
$nupkg = "out/$PackageId.$v.nupkg"
if (-not (Test-Path $nupkg)) { throw "$nupkg was not produced" }

$unzip = "out/unzip-$PackageId"
if (Test-Path $unzip) { Remove-Item $unzip -Recurse -Force }
Expand-Archive $nupkg $unzip
$skipDlls = @('Jai_FactoryDotNET.dll','log4net.dll','SpinnakerNET_v140.dll','FlyCapture2Managed_v100.dll','WebView2Loader.dll')
foreach ($dll in (Get-ChildItem $unzip -Recurse -Filter *.dll)) {
    if ($skipDlls -contains $dll.Name) { continue }
    if ($dll.FullName -match 'contentFiles|runtimes|GocatorSDK') { continue }
    $fv = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll.FullName)
    $dllVer = "$($fv.FileMajorPart).$($fv.FileMinorPart).$($fv.FileBuildPart)"
    if ($dllVer -eq '0.0.0') { throw "$($dll.Name) has version 0.0.0 - version stamping failed" }
    if ($dllVer -ne $v) { throw "$($dll.Name) is $dllVer, package is $v - version mismatch" }
}
Write-Host 'DLL versions verified'

if ($env:VM_PUBLISHING_DRIVE) {
    $archive = Join-Path $env:VM_PUBLISHING_DRIVE "Nuget_Packages\$PackageId\$v"
    New-Item $archive -ItemType Directory -Force | Out-Null
    Copy-Item $nupkg $archive
    Write-Host "Archived to $archive"
}

dotnet nuget push $nupkg --source https://nuget.pkg.github.com/Videometer/index.json --api-key $env:VM_FEED_TOKEN --skip-duplicate
if ($LASTEXITCODE -ne 0) { throw 'push failed' }
Write-Host "Published $PackageId $v"
