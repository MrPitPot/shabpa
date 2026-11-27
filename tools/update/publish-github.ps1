param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string]$Version,
    [string]$SigningKey = $env:SHABPA_SIGN_KEY,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repo = 'MrPitPot/shabpa'
$tag = "v$Version"
$feed = "https://raw.githubusercontent.com/$repo/"
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$bash = Join-Path $env:ProgramFiles 'Git/bin/bash.exe'
$openssl = Join-Path $env:ProgramFiles 'Git/usr/bin/openssl.exe'
$cygpath = Join-Path $env:ProgramFiles 'Git/usr/bin/cygpath.exe'
$priorGithubToken = $env:GH_TOKEN
$priorSigningKey = $env:SHABPA_SIGN_KEY
$priorNotesFa = $env:SHABPA_NOTES_FA
$priorNotesEn = $env:SHABPA_NOTES_EN
$priorUrls = $env:SHABPA_URLS

function Check-Package([string]$path) {
    $manifestPath = Join-Path $path 'latest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $installer = Get-Item -LiteralPath (Join-Path $path 'ShabpaSetup.exe')
    if ($manifest.version -ne $Version -or $installer.VersionInfo.FileVersion -ne "$Version.0" -or
        $manifest.urls[0] -ne "https://github.com/$repo/releases/download/$tag/ShabpaSetup.exe" -or
        $manifest.size -ne $installer.Length -or (Get-FileHash -LiteralPath $installer.FullName).Hash -ne $manifest.sha256) {
        throw 'Release manifest and installer do not match.'
    }
    $pub = [regex]::Match((Get-Content src/Shabpa/Core/UpdateManifest.cs -Raw), 'PublicKeyBase64\s*=\s*"([^"]+)"').Groups[1].Value
    $scratch = New-Item -ItemType Directory -Path (Join-Path $root ('obj/verify-' + [Guid]::NewGuid().ToString('N'))) -Force
    try {
        $publicFile = Join-Path $scratch.FullName 'public.pem'
        $signature = Join-Path $scratch.FullName 'signature.der'
        [IO.File]::WriteAllLines($publicFile, @('-----BEGIN PUBLIC KEY-----', $pub, '-----END PUBLIC KEY-----'))
        [IO.File]::WriteAllBytes($signature, [Convert]::FromBase64String((Get-Content -LiteralPath (Join-Path $path 'latest.json.sig') -Raw).Trim()))
        & $openssl dgst -sha256 -verify $publicFile -signature $signature $manifestPath
    } finally {
        Remove-Item -LiteralPath $publicFile, $signature -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $scratch.FullName -ErrorAction SilentlyContinue
    }
    foreach ($language in 'fa','en') {
        $notes = (Get-Content -LiteralPath "patch-note-$Version.$language.md" -Raw).Trim()
        if (!$notes -or $manifest.notes.$language -cne $notes) { throw "Patch notes do not match for $language." }
    }
}

Push-Location $root
try {
    $releaseFolder = Join-Path $root 'installer'
    if ($Check) {
        Check-Package $releaseFolder
        return
    }
    if (git status --porcelain) { throw 'Commit the release source and both patch notes before publishing.' }
    if (!(Test-Path -LiteralPath $SigningKey -PathType Leaf)) { throw 'Set SHABPA_SIGN_KEY to the local release signing key.' }
    foreach ($project in 'src/Shabpa/Shabpa.csproj','src/Setup/Setup.csproj') {
        $projectXml = [xml](Get-Content -LiteralPath $project -Raw)
        if ($Version -notin @($projectXml.Project.PropertyGroup.Version)) { throw "Update the version in $project." }
    }
    if (!(Select-String -Path build-installer.sh -Pattern ('^VERSION=' + [regex]::Escape($Version) + '$'))) {
        throw 'Update the version in build-installer.sh.'
    }
    foreach ($language in 'fa','en') {
        if (!(Test-Path -LiteralPath "patch-note-$Version.$language.md" -PathType Leaf)) { throw "Missing patch notes for $language." }
    }

    if (!$env:GH_TOKEN) {
        $env:GIT_TERMINAL_PROMPT = '0'
        $env:GCM_INTERACTIVE = 'never'
        $credential = "protocol=https`nhost=github.com`n`n" | git credential fill | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
        if (!$credential) { throw 'No GitHub credential is available.' }
        $env:GH_TOKEN = $credential.Substring(9)
        Remove-Variable credential
    }
    $latest = gh api "repos/$repo/releases/latest" --jq .tag_name
    if ([version]$latest.TrimStart('v') -ge [version]$Version) { throw "Version must be newer than $latest." }
    if (git ls-remote --tags "https://github.com/$repo.git" "refs/tags/$tag") { throw "Tag $tag already exists on GitHub." }

    dotnet test tests/Shabpa.Tests/Shabpa.Tests.csproj --filter 'FullyQualifiedName~Update|FullyQualifiedName~InstallerTests|FullyQualifiedName~PayloadTests'
    $staging = Join-Path $root 'obj/installer'
    if (Test-Path -LiteralPath $staging) {
        $resolved = (Resolve-Path -LiteralPath $staging).Path
        if ($resolved -ne $staging) { throw 'Unexpected installer staging path.' }
        Move-Item -LiteralPath $resolved -Destination (Join-Path $root ('obj/installer-' + [Guid]::NewGuid().ToString('N')))
    }
    $env:SHABPA_SIGN_KEY = & $cygpath -u (Resolve-Path -LiteralPath $SigningKey).Path
    $env:SHABPA_NOTES_FA = "patch-note-$Version.fa.md"
    $env:SHABPA_NOTES_EN = "patch-note-$Version.en.md"
    $env:SHABPA_URLS = "https://github.com/$repo/releases/download/$tag/ShabpaSetup.exe"
    & $bash build-installer.sh
    & $bash tools/update/make-bundle.sh
    Check-Package $releaseFolder

    $body = Join-Path $root "obj/release-notes-$Version.md"
    $fa = Get-Content -LiteralPath $env:SHABPA_NOTES_FA -Raw
    $en = Get-Content -LiteralPath $env:SHABPA_NOTES_EN -Raw
    [IO.File]::WriteAllText($body, ($fa.TrimEnd() + "`n`n---`n`n" + $en))
    $remoteUrl = "https://github.com/$repo.git"
    $commit = git rev-parse HEAD
    git push $remoteUrl "${commit}:refs/tags/$tag"
    $assets = @('ShabpaSetup.exe','ShabpaSetup.exe.sha256','latest.json','latest.json.sig','ShabpaUpdate.zip' | ForEach-Object { Join-Path $releaseFolder $_ })
    $assets += @($env:SHABPA_NOTES_FA, $env:SHABPA_NOTES_EN)
    gh release create $tag @assets --repo $repo --verify-tag --draft --title "Shabpa $Version" --notes-file $body
    $draft = gh release view $tag --repo $repo --json isDraft,assets | ConvertFrom-Json
    if (!$draft.isDraft -or $draft.assets.Count -ne $assets.Count) { throw 'Release upload is incomplete.' }
    foreach ($asset in $assets) {
        $file = Get-Item -LiteralPath $asset
        if ($file.Length -ne ($draft.assets | Where-Object name -eq $file.Name).size) { throw "Incomplete asset: $($file.Name)" }
    }
    gh release edit $tag --repo $repo --draft=false --latest
    $download = Invoke-WebRequest $env:SHABPA_URLS -Method Head -TimeoutSec 30
    if ($download.StatusCode -ne 200) { throw 'Public installer is unavailable.' }

    for ($attempt = 0; $attempt -lt 36; $attempt++) {
        try {
            $feedRevision = (gh api "repos/$repo/git/ref/heads/updates" --jq .object.sha).Trim()
            if ($feedRevision -notmatch '^[a-f0-9]{40}$') { throw 'Invalid update feed revision.' }
            $public = Invoke-RestMethod "$feed$feedRevision/latest.json" -TimeoutSec 10
            if ($public.version -eq $Version) {
                $website = Invoke-RestMethod "https://shabpa.ir/api/latest?release=$Version" -TimeoutSec 10
                if ($website.version -eq $Version -and $website.sha256 -eq $public.sha256) {
                    Write-Output "https://github.com/$repo/releases/tag/$tag"
                    return
                }
            }
        } catch {
            Write-Verbose "Waiting for GitHub and shabpa.ir to agree: $($_.Exception.Message)"
        }
        Start-Sleep -Seconds 5
    }
    throw 'Release is public, but GitHub and shabpa.ir have not both confirmed the new version yet.'
} finally {
    $env:GH_TOKEN = $priorGithubToken
    $env:SHABPA_SIGN_KEY = $priorSigningKey
    $env:SHABPA_NOTES_FA = $priorNotesFa
    $env:SHABPA_NOTES_EN = $priorNotesEn
    $env:SHABPA_URLS = $priorUrls
    Pop-Location
}
