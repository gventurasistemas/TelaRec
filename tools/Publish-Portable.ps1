[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'src\TelaRec.App.Wpf\TelaRec.App.Wpf.csproj'
$solutionPath = Join-Path $repoRoot 'TelaRec.sln'
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$licensePath = Join-Path $repoRoot 'LICENSE'
$thirdPartyNoticesPath = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\portable'))

function Assert-SafeChildPath {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Parent)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $fullParent + [IO.Path]::DirectorySeparatorChar
    if ($fullPath -eq $fullParent -or -not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "A operação foi bloqueada: '$fullPath' não é filho seguro de '$fullParent'."
    }
    return $fullPath
}

function Remove-ValidatedDirectory {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Parent)

    $safePath = Assert-SafeChildPath -Path $Path -Parent $Parent
    if (Test-Path -LiteralPath $safePath) {
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}

function Get-RequiredVersion {
    param([Parameter(Mandatory)][string]$Commit)

    if (-not (Test-Path -LiteralPath $propsPath)) { throw "Versão ausente: $propsPath não existe." }
    [xml]$props = Get-Content -LiteralPath $propsPath
    $baseVersion = [string]$props.Project.PropertyGroup.TelaRecReleaseVersion
    if ([string]::IsNullOrWhiteSpace($baseVersion)) { throw 'Versão-base ausente em Directory.Build.props.' }
    $tag = [string]((& git -C $repoRoot tag --points-at HEAD | Select-Object -First 1))
    $version = if ([string]::IsNullOrWhiteSpace($tag)) { "$baseVersion-dev.$($Commit.Substring(0, 12))" } else { $tag.Trim() }
    if ($version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "Versão inválida para distribuição: '$version'." }
    return $version
}

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments, [Parameter(Mandatory)][string]$LogPath)

    & dotnet @Arguments *> $LogPath
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $LogPath
        throw "dotnet $($Arguments -join ' ') falhou. Consulte $LogPath."
    }
}

function Get-RelativePath {
    param([string]$BasePath, [string]$Path)
    $baseUri = [Uri]($BasePath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar)
    $pathUri = [Uri]$Path
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Write-Manifest {
    param([string]$PublishDirectory, [string]$ManifestPath)

    $entries = Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File |
        Where-Object { $_.Name -ne 'MANIFEST.sha256' } |
        Sort-Object FullName |
        ForEach-Object {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash *$(Get-RelativePath -BasePath $PublishDirectory -Path $_.FullName)"
        }
    [IO.File]::WriteAllLines($ManifestPath, [string[]]$entries, [Text.UTF8Encoding]::new($false))
}

function Test-Manifest {
    param([string]$PublishDirectory, [string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath)) { throw "Manifesto ausente: $ManifestPath" }
    foreach ($line in Get-Content -LiteralPath $ManifestPath) {
        if ($line -notmatch '^([a-f0-9]{64}) \*(.+)$') { throw "Linha inválida no manifesto: $line" }
        $relative = $Matches[2]
        if ($relative.Contains('..') -or [IO.Path]::IsPathRooted($relative)) { throw "Caminho inseguro no manifesto: $relative" }
        $file = Join-Path $PublishDirectory $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Arquivo do manifesto ausente: $relative" }
        $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $Matches[1]) { throw "Hash divergente: $relative" }
    }
}

function Test-PublishContents {
    param([string]$PublishDirectory, [string]$Version)

    $required = @('TelaRec.App.Wpf.exe', 'TelaRec.App.Wpf.dll', 'TelaRec.App.Wpf.runtimeconfig.json', 'TelaRec.App.Wpf.deps.json', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'tools\ffmpeg.exe', 'tools\FFMPEG-LICENSE.txt', 'tools\FFMPEG-BUILD-README.txt')
    foreach ($name in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory $name) -PathType Leaf)) { throw "Arquivo obrigatório ausente: $name" }
    }
    $forbiddenExtensions = @('.pdb', '.cs', '.csproj', '.sln', '.user', '.suo')
    $forbiddenNames = @('ffprobe.exe', 'ffplay.exe')
    $unexpected = Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | Where-Object {
        $forbiddenExtensions -contains $_.Extension.ToLowerInvariant() -or
        $forbiddenNames -contains $_.Name.ToLowerInvariant() -or
        $_.Name -match '(?i)(development|testhost|xunit)'
    }
    if ($unexpected) { throw "Arquivos proibidos na publicação: $($unexpected.FullName -join ', ')" }
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishDirectory 'TelaRec.App.Wpf.exe')).ProductVersion
    if ([string]::IsNullOrWhiteSpace($fileVersion) -or -not $fileVersion.StartsWith($Version, [StringComparison]::Ordinal)) {
        throw "Versão inesperada no executável: '$fileVersion'; esperada '$Version'."
    }
}

function New-DeterministicZip {
    param([string]$SourceDirectory, [string]$ZipPath, [DateTimeOffset]$Timestamp)

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File | Sort-Object FullName | ForEach-Object {
            $entry = $archive.CreateEntry((Get-RelativePath -BasePath $SourceDirectory -Path $_.FullName), [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $Timestamp
            $destination = $entry.Open()
            $source = [IO.File]::OpenRead($_.FullName)
            try { $source.CopyTo($destination) }
            finally { $source.Dispose(); $destination.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

function Add-DistributionDocuments {
    param([Parameter(Mandatory)][string]$PublishDirectory)

    foreach ($document in @($licensePath, $thirdPartyNoticesPath)) {
        if (-not (Test-Path -LiteralPath $document -PathType Leaf)) { throw "Documento obrigatório ausente: $document" }
        Copy-Item -LiteralPath $document -Destination (Join-Path $PublishDirectory (Split-Path -Leaf $document)) -Force
    }
}

function Test-PortableStartup {
    param([string]$Executable, [string]$WorkingDirectory)

    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $WorkingDirectory
    $start.EnvironmentVariables['DOTNET_ROOT'] = (Join-Path $WorkingDirectory '__dotnet_sdk_not_present__')
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $process = [Diagnostics.Process]::Start($start)
    try {
        Start-Sleep -Seconds 3
        if ($process.HasExited) { throw "O executável portátil encerrou durante o startup (código $($process.ExitCode))." }
    }
    finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
    }
}

$commit = [string](& git -C $repoRoot rev-parse HEAD)
$commit = $commit.Trim()
$version = Get-RequiredVersion -Commit $commit
$packageName = "TelaRec-$version-win-x64"
$runRoot = Assert-SafeChildPath -Path (Join-Path $artifactsRoot $packageName) -Parent $artifactsRoot
$publishDirectory = Join-Path $runRoot 'publish'
$reproduciblePublishDirectory = Join-Path $runRoot 'publish-reproducibility-check'
$verificationDirectory = Join-Path $runRoot 'verification\TelaRec Portable Verify'
$zipPath = Join-Path $runRoot "$packageName.zip"
$manifestPath = Join-Path $runRoot 'MANIFEST.sha256'
$zipHashPath = "$zipPath.sha256"

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
Remove-ValidatedDirectory -Path $runRoot -Parent $artifactsRoot
New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null

Invoke-Dotnet -Arguments @('test', $solutionPath, '--verbosity', 'minimal') -LogPath (Join-Path $runRoot 'tests.log')
Invoke-Dotnet -Arguments @('publish', $projectPath, '-c', 'Release', '-r', 'win-x64', '-p:PublishProfile=PortableWinX64', "-p:PublishDir=$publishDirectory", "-p:Version=$version", "-p:SourceRevisionId=$commit", "-p:InformationalVersion=$version+$commit", '-p:ContinuousIntegrationBuild=true', '-p:DebugSymbols=false', '-p:DebugType=none', '--verbosity', 'minimal') -LogPath (Join-Path $runRoot 'publish.log')

Add-DistributionDocuments -PublishDirectory $publishDirectory
Test-PublishContents -PublishDirectory $publishDirectory -Version $version
$epochText = [string](& git -C $repoRoot show -s --format=%ct HEAD)
$epoch = [long]$epochText.Trim()
$timestamp = [DateTimeOffset]::FromUnixTimeSeconds($epoch)
$release = [ordered]@{
    Version = $version
    Commit = $commit
    RuntimeIdentifier = 'win-x64'
    SelfContained = $true
    PublishSingleFile = $false
    PublishTrimmed = $false
    ZipTimestampUtc = $timestamp.UtcDateTime.ToString('O')
}
$releasePath = Join-Path $publishDirectory 'RELEASE.json'
[IO.File]::WriteAllText($releasePath, ($release | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

Write-Manifest -PublishDirectory $publishDirectory -ManifestPath $manifestPath
Test-Manifest -PublishDirectory $publishDirectory -ManifestPath $manifestPath

# Uma segunda publicação no mesmo commit deve produzir exatamente o mesmo manifesto.
New-Item -ItemType Directory -Force -Path $reproduciblePublishDirectory | Out-Null
Invoke-Dotnet -Arguments @('publish', $projectPath, '-c', 'Release', '-r', 'win-x64', '-p:PublishProfile=PortableWinX64', "-p:PublishDir=$reproduciblePublishDirectory", "-p:Version=$version", "-p:SourceRevisionId=$commit", "-p:InformationalVersion=$version+$commit", '-p:ContinuousIntegrationBuild=true', '-p:DebugSymbols=false', '-p:DebugType=none', '--verbosity', 'minimal') -LogPath (Join-Path $runRoot 'publish-reproducibility.log')
Add-DistributionDocuments -PublishDirectory $reproduciblePublishDirectory
Test-PublishContents -PublishDirectory $reproduciblePublishDirectory -Version $version
Copy-Item -LiteralPath $releasePath -Destination (Join-Path $reproduciblePublishDirectory 'RELEASE.json')
$reproducibleManifest = Join-Path $runRoot 'MANIFEST.reproducibility.sha256'
Write-Manifest -PublishDirectory $reproduciblePublishDirectory -ManifestPath $reproducibleManifest
if (Compare-Object (Get-Content -LiteralPath $manifestPath) (Get-Content -LiteralPath $reproducibleManifest)) {
    throw 'Publicações do mesmo commit produziram manifestos diferentes; o pacote não é reproduzível.'
}
Remove-ValidatedDirectory -Path $reproduciblePublishDirectory -Parent $runRoot

Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $publishDirectory 'MANIFEST.sha256')
New-DeterministicZip -SourceDirectory $publishDirectory -ZipPath $zipPath -Timestamp $timestamp
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zipHashPath, "$zipHash *$([IO.Path]::GetFileName($zipPath))`n", [Text.UTF8Encoding]::new($false))

Remove-ValidatedDirectory -Path $verificationDirectory -Parent $runRoot
Expand-Archive -LiteralPath $zipPath -DestinationPath $verificationDirectory -Force
Test-Manifest -PublishDirectory $verificationDirectory -ManifestPath (Join-Path $verificationDirectory 'MANIFEST.sha256')
Test-PortableStartup -Executable (Join-Path $verificationDirectory 'TelaRec.App.Wpf.exe') -WorkingDirectory $verificationDirectory

$sizeMiB = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2)
Write-Host "PASS: $zipPath ($sizeMiB MiB)"
Write-Host "Manifesto: $manifestPath"
Write-Host "SHA-256: $zipHashPath"
