$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

# A fresh output directory prevents old local files from entering the ZIP.
$buildOutput = Join-Path $PSScriptRoot ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
$packageDirectory = Join-Path $PSScriptRoot 'dist'
$packagePath = Join-Path $packageDirectory 'TibidleMultiManager-win-x64.zip'

dotnet publish ./src/TibidleMultiManager.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $buildOutput
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou; nenhum ZIP foi criado.' }

foreach ($document in @('LICENSE', 'README.md', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $buildOutput
}
New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Compress-Archive -Path (Join-Path $buildOutput '*') -DestinationPath $packagePath -Force
Write-Output "Pacote criado: $packagePath"
