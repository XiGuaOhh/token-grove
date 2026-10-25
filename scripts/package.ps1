param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory=Join-Path $projectRoot 'dist' }
$exe=Join-Path $projectRoot 'Token Grove.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the app before packaging.' }
$version=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion -replace '\.0$',''
$staging=Join-Path $OutputDirectory ('staging-'+[Guid]::NewGuid().ToString('N'))
$appFolder=Join-Path $staging 'token-grove-desktop'
New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
try {
  foreach($name in @('Token Grove.exe','app.ico','app.manifest','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $appFolder }
  Copy-Item -LiteralPath (Join-Path $projectRoot 'fonts') -Destination $appFolder -Recurse
  Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $appFolder -Recurse
  Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/使用说明.md') -Destination (Join-Path $appFolder '使用说明.md')
  Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/PERFORMANCE.md') -Destination (Join-Path $appFolder '性能实测.md')
  $archive=Join-Path $OutputDirectory ('Token-Grove-v'+$version+'-Windows-x64.zip')
  Compress-Archive -LiteralPath $appFolder -DestinationPath $archive -CompressionLevel Optimal -Force
  $checksum=Get-FileHash -LiteralPath $archive -Algorithm SHA256
  ($checksum.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
  Get-Item -LiteralPath $archive
} finally {
  $verified=[IO.Path]::GetFullPath($staging)
  $parent=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
  if ($verified.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($verified).StartsWith('staging-')) { Remove-Item -LiteralPath $verified -Recurse -Force }
}
