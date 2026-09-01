$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$external = Join-Path $root 'external'
New-Item -ItemType Directory -Force -Path $external | Out-Null

$repos = @(
    @{ Name = 'san9-toolkit'; Url = 'https://github.com/cdd1037/san9-toolkit.git' },
    @{ Name = 'san9-copilot-re'; Url = 'https://github.com/jslhcl/jslhcl.github.io.git' },
    @{ Name = 'kaodata'; Url = 'https://github.com/tzengyuxio/kaodata.git' },
    @{ Name = 'san9pk-win10'; Url = 'https://github.com/leejeonghun/san9pk-win10.git' },
    @{ Name = 'san9-notes'; Url = 'https://github.com/ChihChiu29/chihchiu29.github.io.git' }
)

foreach ($repo in $repos) {
    $dest = Join-Path $external $repo.Name
    if (Test-Path (Join-Path $dest '.git')) {
        Write-Host "Updating $($repo.Name)..."
        git -C $dest fetch --all --prune
        git -C $dest pull --ff-only
    }
    else {
        Write-Host "Cloning $($repo.Name)..."
        git clone --depth 1 $repo.Url $dest
    }
}

Write-Host "External San9 research repositories are available under: $external"
