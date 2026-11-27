param(
 [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='0.8.4',
 [string]$OutputDirectory,
 [switch]$Publish
)
$ErrorActionPreference='Stop'
$sourceRoot=$PSScriptRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path $sourceRoot ".build/release/Bardola-$Version"}
$releaseRoot=[IO.Path]::GetFullPath($OutputDirectory)
$appDirectory=Join-Path $releaseRoot 'app'
$feedDirectory=Join-Path $releaseRoot 'feed'
$launcherDirectory=Join-Path $releaseRoot 'launcher'
$bootstrapDirectory=Join-Path $sourceRoot '.build/bootstrap'
New-Item -ItemType Directory -Force -Path $releaseRoot,$bootstrapDirectory | Out-Null
Push-Location $sourceRoot
try{
 $installer=Join-Path $releaseRoot 'Instalar-Bardola.exe'
 $apiZip=Join-Path $releaseRoot "Bardola-API-$Version.zip"
 $prepared=$Publish -and (Test-Path -LiteralPath $installer) -and (Test-Path -LiteralPath $apiZip) -and (Test-Path -LiteralPath (Join-Path $releaseRoot 'latest.json')) -and (Test-Path -LiteralPath (Join-Path $releaseRoot "bardola-$Version.zip"))
 # Publica exatamente os arquivos já preparados e revisados, sem recompilar.
 if(!$prepared){
 & dotnet publish Desktop/NexoLoL.Desktop.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $appDirectory --nologo -v:q
 if($LASTEXITCODE){throw 'Falha ao compilar app.'}
 & dotnet run --project UpdatePublisher/Bardo.UpdatePublisher.csproj -c Release -- --app $appDirectory --feed $feedDirectory --version $Version
 if($LASTEXITCODE){throw 'Falha ao assinar atualização.'}
 foreach($file in Get-ChildItem -LiteralPath $bootstrapDirectory -File){Remove-Item -LiteralPath $file.FullName}
 Copy-Item -LiteralPath (Join-Path $feedDirectory 'latest.json') -Destination $bootstrapDirectory
 Copy-Item -LiteralPath (Join-Path $feedDirectory "bardola-$Version.zip") -Destination $bootstrapDirectory
 & dotnet publish Launcher/Bardo.Launcher.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $launcherDirectory --nologo -v:q
 if($LASTEXITCODE){throw 'Falha ao compilar instalador.'}
 Copy-Item -LiteralPath (Join-Path $launcherDirectory 'Bardola.exe') -Destination $installer -Force
 Copy-Item -LiteralPath (Join-Path $feedDirectory 'latest.json') -Destination $releaseRoot -Force
 Copy-Item -LiteralPath (Join-Path $feedDirectory "bardola-$Version.zip") -Destination $releaseRoot -Force
 $apiDirectory=Join-Path $releaseRoot 'api'
 & dotnet publish Api/Bardo.Api.csproj -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $apiDirectory --nologo -v:q
 if($LASTEXITCODE){throw 'Falha ao compilar a API.'}
 Copy-Item -LiteralPath 'docs/api-perfis.md','LICENSE','SOURCE.txt','THIRD_PARTY_NOTICES.md' -Destination $apiDirectory
 Compress-Archive -Path (Join-Path $apiDirectory '*') -DestinationPath $apiZip -Force
 }
 if($Publish){
   & gh release create "v$Version" $installer $apiZip (Join-Path $releaseRoot 'latest.json') (Join-Path $releaseRoot "bardola-$Version.zip") --repo robertfxbr/Bardola --target (git rev-parse HEAD) --latest --title "Bardola $Version" --notes-file (Join-Path $sourceRoot 'RELEASE_NOTES.md')
   if($LASTEXITCODE){throw 'Falha na publicação da Release.'}
 }
 Write-Output "Entrega preparada: $releaseRoot"
}finally{Pop-Location}
