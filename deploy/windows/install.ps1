<#
  Tracker روی ویندوز سرور: دو سرویس ویندوز (WinSW) + یک سایت IIS (URL Rewrite + ARR) + SSL (win-acme)
    tracker-api  ASP.NET Core (backend/Tracker.Api)   روی 127.0.0.1:$ApiPort
    tracker-web  Next.js standalone (frontend)    روی 127.0.0.1:$WebPort

  پیش‌نیاز: .NET 10 Runtime (ASP.NET Core Hosting Bundle)، Node.js 20+، IIS با URL Rewrite و ARR (Proxy فعال)، SQL Server، WinSW، win-acme

  آماده‌سازی (روی همان سرور یا جای دیگر):
    dotnet publish backend\Tracker.Api -c Release -r win-x64 --self-contained false -o C:\temp\tracker\api
    cd frontend; $env:API_INTERNAL_URL="http://127.0.0.1:$ApiPort"; npm ci; npm run build
    # خروجی Next.js: .next\standalone + .next\static + public
  کنار فایل‌های API یک appsettings.Production.json بگذارید (نمونه: deploy\windows\appsettings.Production.example.json).

  اجرا (PowerShell با Administrator):
    .\deploy\windows\install.ps1 -Api C:\temp\tracker\api -Frontend .\frontend -Domain tracker.rhinox.ir -WinSW C:\tools\WinSW-x64.exe -Email you@example.com
#>
param(
  [Parameter(Mandatory)] [string]$Api,
  [Parameter(Mandatory)] [string]$Frontend,
  [Parameter(Mandatory)] [string]$Domain,
  [Parameter(Mandatory)] [string]$WinSW,
  [string]$Email,
  [string]$AppDir = 'C:\apps\tracker',
  [int]$ApiPort = 5086,
  [int]$WebPort = 3100,
  [string]$Node = (Get-Command node -ErrorAction SilentlyContinue).Source,
  [string]$Wacs
)
$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'
Import-Module WebAdministration
if (-not $Node) { throw 'node.exe پیدا نشد (-Node بدهید).' }

function Install-Svc($id, $name, $dir, $exePath, $arguments, $envs) {
  if (Get-Service $id -ErrorAction SilentlyContinue) { Stop-Service $id -Force; Start-Sleep 2 }
  $wrapper = Join-Path $dir "$id.exe"
  if (-not (Test-Path $wrapper)) { Copy-Item $WinSW $wrapper }
  $envXml = ($envs.GetEnumerator() | % { "  <env name=""$($_.Key)"" value=""$($_.Value)""/>" }) -join "`n"
  @"
<service>
  <id>$id</id><name>$name</name>
  <executable>$exePath</executable><arguments>$arguments</arguments><workingdirectory>$dir</workingdirectory>
$envXml
  <startmode>Automatic</startmode><delayedAutoStart>true</delayedAutoStart>
  <onfailure action="restart" delay="10 sec"/><onfailure action="restart" delay="30 sec"/><resetfailure>1 hour</resetfailure>
  <logpath>$dir\logs</logpath><log mode="roll-by-size"><sizeThreshold>10240</sizeThreshold><keepFiles>4</keepFiles></log>
</service>
"@ | Set-Content (Join-Path $dir "$id.xml") -Encoding UTF8
  if (-not (Get-Service $id -ErrorAction SilentlyContinue)) { & $wrapper install | Out-Host }
  Start-Service $id; Start-Sleep 3
  "$id : " + (Get-Service $id).Status
}

# --- نسخه‌ی قبلی (یک سرویس) را کنار بگذار: همان دیتابیس و کاربرها خودکار منتقل می‌شوند ---
foreach ($old in 'tracker-collector'.Split(',')) {
  if ($old -and (Get-Service $old -ErrorAction SilentlyContinue)) { Stop-Service $old -Force; Set-Service $old -StartupType Disabled; "old service '$old' stopped and disabled" }
}

# --- API ---
$apiDir = Join-Path $AppDir 'api'; New-Item -ItemType Directory -Force -Path $apiDir | Out-Null
if (Get-Service 'tracker-api' -ErrorAction SilentlyContinue) { Stop-Service 'tracker-api' -Force; Start-Sleep 2 }
Copy-Item "$Api\*" $apiDir -Recurse -Force
if (-not (Test-Path "$apiDir\appsettings.Production.json") -and -not (Test-Path "$apiDir\appsettings.Local.json")) { throw "appsettings.Production.json در $apiDir نیست" }
Install-Svc 'tracker-api' 'Tracker API' $apiDir "$apiDir\Tracker.Api.exe" '' @{
  ASPNETCORE_ENVIRONMENT = 'Production'; ASPNETCORE_URLS = "http://127.0.0.1:$ApiPort"
  DOTNET_gcServer = '0'; DOTNET_GCConserveMemory = '7'
}

# --- Next.js standalone ---
$webDir = Join-Path $AppDir 'web'; New-Item -ItemType Directory -Force -Path $webDir | Out-Null
if (Get-Service 'tracker-web' -ErrorAction SilentlyContinue) { Stop-Service 'tracker-web' -Force; Start-Sleep 2 }
Copy-Item "$Frontend\.next\standalone\*" $webDir -Recurse -Force
New-Item -ItemType Directory -Force -Path "$webDir\.next\static" | Out-Null
Copy-Item "$Frontend\.next\static\*" "$webDir\.next\static" -Recurse -Force
if (Test-Path "$Frontend\public") { Copy-Item "$Frontend\public" $webDir -Recurse -Force }
Install-Svc 'tracker-web' 'Tracker Panel' $webDir $Node 'server.js' @{
  NODE_ENV = 'production'; PORT = "$WebPort"; HOSTNAME = '127.0.0.1'; API_INTERNAL_URL = "http://127.0.0.1:$ApiPort"
}

# --- IIS ---
$site = "C:\Sites\$Domain"; New-Item -ItemType Directory -Force -Path $site | Out-Null
(Get-Content "$PSScriptRoot\web.config" -Raw).Replace('{API_PORT}', "$ApiPort").Replace('{WEB_PORT}', "$WebPort") | Set-Content "$site\web.config" -Encoding UTF8
if (-not (Get-Website -Name $Domain)) { New-Website -Name $Domain -PhysicalPath $site -HostHeader $Domain -Port 80 | Out-Null }
foreach ($v in 'HTTP_X_FORWARDED_PROTO', 'HTTP_X_FORWARDED_HOST') {
  $exists = Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $Domain -Filter 'system.webServer/rewrite/allowedServerVariables' -Name Collection | ? { $_.name -eq $v }
  if (-not $exists) { Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $Domain -Filter 'system.webServer/rewrite/allowedServerVariables' -Name '.' -Value @{ name = $v } }
}

# --- SSL رایگان (Let's Encrypt با win-acme) ---
if (-not (Get-WebBinding -Name $Domain -Protocol https -ErrorAction SilentlyContinue)) {
  if (-not $Wacs) { $Wacs = Get-ChildItem C:\Tools, 'C:\Program Files' -Recurse -Filter wacs.exe -ErrorAction SilentlyContinue | Select -First 1 -ExpandProperty FullName }
  if ($Wacs) { & $Wacs --source iis --siteid (Get-Website -Name $Domain).id --host $Domain --installation iis --accepttos --emailaddress $Email | Select -Last 4 | Out-Host }
  else { Write-Warning 'win-acme پیدا نشد؛ SSL را دستی بگیرید.' }
}
try { "api health: " + (Invoke-WebRequest -UseBasicParsing -TimeoutSec 15 "http://127.0.0.1:$ApiPort/health").StatusCode } catch { "api health: $($_.Exception.Message)" }
try { "panel: " + (Invoke-WebRequest -UseBasicParsing -TimeoutSec 15 -MaximumRedirection 0 "http://127.0.0.1:$WebPort/login").StatusCode } catch { "panel: $($_.Exception.Message)" }
"https://$Domain"
