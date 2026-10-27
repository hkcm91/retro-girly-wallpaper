param(
  [string]$WebSource = (Join-Path $PSScriptRoot 'web'),
  [string]$OutputPath = (Join-Path $PSScriptRoot 'build/PetalOS'),
  [string]$WebViewPackage = (Join-Path $env:USERPROFILE '.nuget/packages/microsoft.web.webview2/1.0.4078.44')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $WebViewPackage 'lib/net462/Microsoft.Web.WebView2.Core.dll'))) {
  $WebViewPackage = Join-Path $PSScriptRoot 'dependencies/webview2'
  New-Item -ItemType Directory -Force -Path $WebViewPackage | Out-Null
  $packageFile = Join-Path $PSScriptRoot 'dependencies/webview2.zip'
  Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4078.44/microsoft.web.webview2.1.0.4078.44.nupkg' -OutFile $packageFile
  Expand-Archive -LiteralPath $packageFile -DestinationPath $WebViewPackage -Force
}
if (-not (Test-Path -LiteralPath (Join-Path $WebSource 'index.html'))) { throw 'WebSource must contain the wallpaper index.html.' }
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$framework = Join-Path $WebViewPackage 'lib/net462'
$compileArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+',
  ('/out:' + (Join-Path $OutputPath 'PetalOS.exe')),
  ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
  ('/win32icon:' + (Join-Path $WebSource 'icons/heart.ico')),
  '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll',
  '/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',
  ('/reference:' + (Join-Path $framework 'Microsoft.Web.WebView2.Core.dll')),
  ('/reference:' + (Join-Path $framework 'Microsoft.Web.WebView2.WinForms.dll')),
  (Join-Path $PSScriptRoot 'Program.cs'))
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
Copy-Item -LiteralPath (Join-Path $framework 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $framework 'Microsoft.Web.WebView2.WinForms.dll') -Destination $OutputPath -Force
Copy-Item -LiteralPath (Join-Path $WebViewPackage 'runtimes/win-x64/native/WebView2Loader.dll') -Destination $OutputPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PetalOS.exe.config') -Destination $OutputPath -Force
New-Item -ItemType Directory -Force -Path (Join-Path $OutputPath 'web'),(Join-Path $OutputPath 'licenses') | Out-Null
foreach ($asset in @('index.html','style.css','app.js','README.html','LICENSE.txt','TEST-RESULTS.txt','START-HERE.txt','thumbnail.png','icons','backgrounds')) {
  $assetPath = Join-Path $WebSource $asset
  if (Test-Path -LiteralPath $assetPath) { Copy-Item -LiteralPath $assetPath -Destination (Join-Path $OutputPath 'web') -Recurse -Force }
}
Copy-Item -LiteralPath (Join-Path $WebViewPackage 'LICENSE.txt') -Destination (Join-Path $OutputPath 'licenses/WebView2-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $WebViewPackage 'NOTICE.txt') -Destination (Join-Path $OutputPath 'licenses/WebView2-NOTICE.txt') -Force
Write-Output ('Built: ' + (Join-Path $OutputPath 'PetalOS.exe'))
