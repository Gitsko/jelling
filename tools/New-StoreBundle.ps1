<#
.SYNOPSIS
    Builds the Microsoft Store package: one .msix each for x64 and ARM64, bundled into Jelling.msixbundle.
.DESCRIPTION
    Partner Center takes the bundle as it is and signs it; the build stays unsigned. dotnet build can only
    build one platform per run, so the bundle is put together with makeappx afterwards.
    Sets the version in Package.appxmanifest for the build and puts the file back afterwards.
#>
param(
    # Three parts, as in the release tag; the Store wants a fourth part that is always 0. Defaults to the
    # project's version, so a build outside a release still gets one
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Output = (Join-Path $PWD 'Jelling.msixbundle')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Jelling.App/Jelling.App.csproj'
$manifestPath = Join-Path $root 'src/Jelling.App/Package.appxmanifest'
$work = Join-Path ([IO.Path]::GetTempPath()) "jelling-store-$([Guid]::NewGuid().ToString('N'))"
$packages = Join-Path $work 'packages'
if (-not $Version) {
    $Version = @(([xml](Get-Content -Raw $project)).Project.PropertyGroup.Version | Where-Object { $_ })[0]
}
$packageVersion = "$Version.0"

# A text replace keeps the file's layout and comments, which XmlDocument.Save would reflow
$manifest = Get-Content -Raw $manifestPath
$identityVersion = '(<Identity\b[^>]*\bVersion=")[^"]*'
if ($manifest -notmatch $identityVersion) { throw 'Identity Version not found in Package.appxmanifest' }

New-Item -ItemType Directory $packages | Out-Null
try {
    [IO.File]::WriteAllText($manifestPath, ($manifest -replace $identityVersion, "`${1}$packageVersion"))
    foreach ($platform in 'x64', 'ARM64') {
        $rid = "win-$($platform.ToLower())"
        # The trailing separator is required; without it MSBuild takes the folder for a file name prefix
        $packageDir = Join-Path $work "$platform\"
        dotnet build $project -c Release -r $rid -p:Platform=$platform -p:Store=true -p:Version=$Version "-p:AppxPackageDir=$packageDir"
        if ($LASTEXITCODE) { throw "Store build for $platform failed" }
        $msix = @(Get-ChildItem -Recurse $packageDir -Filter *.msix)
        if ($msix.Count -ne 1) { throw "Expected one .msix for $platform, found $($msix.Count)" }
        Copy-Item $msix[0].FullName $packages
    }

    # Comes with the Windows SDK build tools package the project already restores
    $sdkVersion = ([xml](Get-Content -Raw $project)).Project.ItemGroup.PackageReference |
        Where-Object Include -eq 'Microsoft.Windows.SDK.BuildTools' | Select-Object -ExpandProperty Version
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }
    $makeappx = Get-ChildItem (Join-Path $nuget "microsoft.windows.sdk.buildtools/$sdkVersion/bin/*/x64/makeappx.exe") |
        Select-Object -First 1
    if (-not $makeappx) { throw "makeappx.exe not found in Microsoft.Windows.SDK.BuildTools $sdkVersion" }

    & $makeappx.FullName bundle /o /d $packages /bv $packageVersion /p $Output
    if ($LASTEXITCODE) { throw 'makeappx bundle failed' }
}
finally {
    [IO.File]::WriteAllText($manifestPath, $manifest)
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
