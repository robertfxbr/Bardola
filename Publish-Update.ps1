param(
 [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='0.7.3',
 [string]$OutputDirectory,
 [switch]$Publish
)
$ErrorActionPreference='Stop'
$sourceRoot=$PSScriptRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path $sourceRoot ".build/release/$Version"}
$releaseRoot=[IO.Path]::GetFullPath($OutputDirectory)
$appDirectory=Join-Path $releaseRoot 'app'
$feedDirectory=Join-Path $releaseRoot 'feed'
$launcherDirectory=Join-Path $releaseRoot 'launcher'
$bootstrapDirectory=Join-Path $sourceRoot '.build/bootstrap'
New-Item -ItemType Directory -Force -Path $releaseRoot,$bootstrapDirectory | Out-Null
Push-Location $sourceRoot
try{
 $installer=Join-Path $releaseRoot 'Instalar-Bardo.gg.exe'
 $prepared=$Publish -and (Test-Path -LiteralPath $installer) -and (Test-Path -LiteralPath (Join-Path $releaseRoot 'latest.json')) -and (Test-Path -LiteralPath (Join-Path $releaseRoot "bardo-$Version.zip"))
 # Publica exatamente os arquivos já preparados e revisados, sem recompilar.
 if(!$prepared){
 & dotnet publish Desktop/NexoLoL.Desktop.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $appDirectory --nologo -v:q
 if($LASTEXITCODE){throw 'Falha ao compilar app.'}
 & dotnet run --project UpdatePublisher/Bardo.UpdatePublisher.csproj -c Release -- --app $appDirectory --feed $feedDirectory --version $Version
 if($LASTEXITCODE){throw 'Falha ao assinar atualização.'}
 foreach($file in Get-ChildItem -LiteralPath $bootstrapDirectory -File){Remove-Item -LiteralPath $file.FullName}
 Copy-Item -LiteralPath (Join-Path $feedDirectory 'latest.json') -Destination $bootstrapDirectory
 Copy-Item -LiteralPath (Join-Path $feedDirectory "bardo-$Version.zip") -Destination $bootstrapDirectory
 & dotnet publish Launcher/Bardo.Launcher.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $launcherDirectory --nologo -v:q
 if($LASTEXITCODE){throw 'Falha ao compilar instalador.'}
 $installer=Join-Path $releaseRoot 'Instalar-Bardo.gg.exe'
 Copy-Item -LiteralPath (Join-Path $launcherDirectory 'Bardo.gg.exe') -Destination $installer -Force
 Copy-Item -LiteralPath (Join-Path $feedDirectory 'latest.json') -Destination $releaseRoot -Force
 Copy-Item -LiteralPath (Join-Path $feedDirectory "bardo-$Version.zip") -Destination $releaseRoot -Force
 }
 if($Publish){
   & gh release create "v$Version" $installer (Join-Path $releaseRoot 'latest.json') (Join-Path $releaseRoot "bardo-$Version.zip") --repo robertfxbr/Bardo.gg --title "Bardo.gg $Version" --notes-file (Join-Path $sourceRoot 'RELEASE_NOTES.md')
   if($LASTEXITCODE){throw 'Falha na publicação da Release.'}
 }
 Write-Output "Entrega preparada: $releaseRoot"
}finally{Pop-Location}
