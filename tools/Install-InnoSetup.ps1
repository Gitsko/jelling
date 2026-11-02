# Installs a pinned Inno Setup for building installer/jelling.iss and returns the path of its compiler, ISCC.exe.
# Used by CI; the hash check is what stops a replaced download, so a new version needs a new hash.
param(
    [string] $Destination = (Join-Path ([IO.Path]::GetTempPath()) 'innosetup')
)
$ErrorActionPreference = 'Stop'

# Signed by the author (Pyrsys B.V.); gh release verify-asset is-7_1_0 <file> --repo jrsoftware/issrc confirms a hash
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe'
$sha256 = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'

$setup = Join-Path ([IO.Path]::GetTempPath()) 'innosetup-setup.exe'
try {
    Invoke-WebRequest $url -OutFile $setup
    $actual = (Get-FileHash $setup -Algorithm SHA256).Hash
    if ($actual -ne $sha256) {
        throw "Inno Setup download has hash $actual, expected $sha256"
    }

    # Waited for, so ISCC exists when the script returns
    $process = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=`"$Destination`"" -Wait -PassThru
    if ($process.ExitCode) {
        throw "Inno Setup installer exited with $($process.ExitCode)"
    }
}
finally {
    Remove-Item $setup -ErrorAction SilentlyContinue
}

$iscc = Join-Path $Destination 'ISCC.exe'
if (-not (Test-Path $iscc)) {
    throw "ISCC.exe not found in $Destination"
}
$iscc
