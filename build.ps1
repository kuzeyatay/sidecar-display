param(
    [switch]$Package,
    [switch]$Test,
    [ValidateSet('Core', 'Desktop', 'Full')][string]$TestMode = 'Core'
)
$ErrorActionPreference = 'Stop'
$appRoot = $PSScriptRoot
Set-Location -LiteralPath $appRoot
$env:DOTNET_CLI_HOME = Join-Path $appRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:NUGET_PACKAGES = Join-Path $appRoot '.tools\nuget'
$sdk = Join-Path $appRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
function Run-Dotnet([string[]]$Arguments) {
    & $sdk @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)" }
}
Run-Dotnet @('restore', 'src\SidecarDisplay.csproj', '--configfile', 'NuGet.Config')
Run-Dotnet @('build', 'src\SidecarDisplay.csproj', '-c', 'Release', '--no-restore')
if ($Test) {
    Run-Dotnet @('restore', 'tests\SidecarDisplay.Tests.csproj', '--configfile', 'NuGet.Config')
    Run-Dotnet @('build', 'tests\SidecarDisplay.Tests.csproj', '-c', 'Release', '--no-restore')
    $testArguments = @("artifacts\$($TestMode.ToLowerInvariant())")
    if ($TestMode -eq 'Core') { $testArguments += '--core' }
    if ($TestMode -eq 'Desktop') { $testArguments += '--live' }
    & $sdk .\tests\bin\Release\net8.0-windows\SidecarDisplay.Tests.dll @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed' }
}
if ($Package) {
    [xml]$project = Get-Content -LiteralPath src\SidecarDisplay.csproj
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a major.minor.patch version' }
    $distRoot = [IO.Path]::GetFullPath((Join-Path $appRoot 'dist'))
    $packageRoot = [IO.Path]::GetFullPath((Join-Path $distRoot "$version\SidecarDisplay"))
    # Clean only this version's staging folder; do not touch another running release.
    if (!$packageRoot.StartsWith($distRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging path' }
    if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
    Run-Dotnet @('publish', 'src\SidecarDisplay.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None', '-p:RestoreConfigFile=NuGet.Config', '-o', $packageRoot)
    $driverZip = Join-Path $appRoot '.tools\vdd.zip'
    if (!(Test-Path -LiteralPath $driverZip)) {
        New-Item -ItemType Directory -Force -Path .tools | Out-Null
        Invoke-WebRequest 'https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VDD.Control.25.7.23.zip' -OutFile $driverZip
    }
    if ((Get-FileHash -LiteralPath $driverZip -Algorithm SHA256).Hash -ne 'A701F2272E9FCF382849B24F913C6DD07597B3B1116525F2E90182F019609154') { throw 'Driver archive checksum mismatch' }
    Expand-Archive -LiteralPath $driverZip -DestinationPath .tools\vdd -Force
    $driverRoot = Join-Path $packageRoot 'driver'
    New-Item -ItemType Directory -Force -Path $driverRoot | Out-Null
    Copy-Item -LiteralPath '.tools\vdd\SignedDrivers\x86\VDD\MttVDD.dll','.tools\vdd\SignedDrivers\x86\VDD\MttVDD.inf','.tools\vdd\SignedDrivers\x86\VDD\mttvdd.cat','.tools\vdd\SignedDrivers\x86\VDD\vdd_settings.xml' -Destination $driverRoot
    Copy-Item -LiteralPath README.md,LICENSE,THIRD-PARTY-NOTICES.txt,CHANGELOG.md,SECURITY.md,CONTRIBUTING.md,VERIFICATION.md -Destination $packageRoot
    Copy-Item -LiteralPath docs -Destination $packageRoot -Recurse
    foreach ($packName in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $pack = Get-ChildItem (Join-Path $env:NUGET_PACKAGES $packName) -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if (!$pack) { throw "Missing runtime pack: $packName" }
        $notices = Join-Path $packageRoot "runtime-notices\$packName"
        New-Item -ItemType Directory -Force -Path $notices | Out-Null
        $licenses = @(Get-ChildItem -LiteralPath $pack.FullName -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' })
        if (!$licenses.Count) { throw "Missing runtime license notices: $packName" }
        $licenses | Copy-Item -Destination $notices
    }
    $archive = Join-Path $distRoot "SidecarDisplay-$version-Windows-x64.zip"
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $archive -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
    Write-Output "Package: $archive"
    Write-Output "SHA256: $hash"
}
