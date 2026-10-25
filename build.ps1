$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$sdk = (& dotnet --list-sdks | Select-Object -Last 1)
if (-not $sdk) { throw 'Building requires a .NET SDK. Running the finished app only requires .NET Framework 4.8.' }
$sdkVersion = ($sdk -split ' ')[0]
$sdkBase = [regex]::Match($sdk,'\[(.*?)\]').Groups[1].Value
$compiler = Join-Path $sdkBase ($sdkVersion + '/Roslyn/bincore/csc.dll')
$references = @('mscorlib','System','System.Core','System.Data','System.Drawing','System.Windows.Forms','System.Web.Extensions','System.Security','System.Net.Http','Microsoft.CSharp') | ForEach-Object { '/r:'+(Join-Path $framework ($_+'.dll')) }
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$output = Join-Path $projectRoot 'Token Grove.exe'
$iconPath = Join-Path $projectRoot 'app.ico'
$iconArgument = if (Test-Path -LiteralPath $iconPath) { '/win32icon:'+$iconPath } else { @() }
& dotnet $compiler /nologo /target:winexe /platform:x64 /optimize+ /debug- /langversion:latest /noconfig /nostdlib+ ('/out:'+$output) ('/win32manifest:'+(Join-Path $projectRoot 'app.manifest')) $iconArgument $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
Get-Item -LiteralPath $output | Select-Object Name,Length
