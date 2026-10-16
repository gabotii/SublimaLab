# Generates the static product and category images referenced by the seed data from the
# storefront's SVG sprite (src/SublimaLab.Blazor/wwwroot/images/art/sprite.svg).
# Re-run after editing the sprite:  pwsh scripts/generate-product-images.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$wwwroot = Join-Path $root 'src/SublimaLab.Blazor/wwwroot'
[xml] $sprite = Get-Content (Join-Path $wwwroot 'images/art/sprite.svg') -Raw

function Write-Art([string] $symbolId, [string] $color, [string] $background, [string] $path) {
    $symbol = $sprite.svg.symbol | Where-Object { $_.id -eq $symbolId }
    if (-not $symbol) { throw "Symbol '$symbolId' not found in sprite." }
    $bg = if ($background) { "<rect width=`"240`" height=`"240`" rx=`"28`" fill=`"$background`"/>" } else { '' }
    # InnerXml repeats the namespace on every child element; declare it once on the root instead.
    $inner = $symbol.InnerXml.Replace(' xmlns="http://www.w3.org/2000/svg"', '')
    $svg = "<svg xmlns=`"http://www.w3.org/2000/svg`" viewBox=`"$($symbol.viewBox)`" color=`"$color`">$bg$inner</svg>"
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, $svg + "`n")
}

$products = @(
    @{ Slug = 'classic-white-t-shirt';     Art = 'tshirt';          Color = '#FFFFFF' },
    @{ Slug = 'classic-black-t-shirt';     Art = 'tshirt';          Color = '#1C1C1E' },
    @{ Slug = 'premium-white-t-shirt';     Art = 'tshirt';          Color = '#FFFFFF' },
    @{ Slug = 'premium-black-t-shirt';     Art = 'tshirt';          Color = '#1C1C1E' },
    @{ Slug = 'colors-collection-t-shirt'; Art = 'tshirt';          Color = '#A9D4F5' },
    @{ Slug = 'white-ceramic-mug';         Art = 'mug';             Color = '#FFFFFF' },
    @{ Slug = 'magic-color-changing-mug';  Art = 'mug';             Color = '#1C1C1E' },
    @{ Slug = 'acrylic-keychain';          Art = 'keychain-rect';   Color = '#EEF3F8' },
    @{ Slug = 'square-keychain';           Art = 'keychain-square'; Color = '#FFFFFF' },
    @{ Slug = 'round-keychain';            Art = 'keychain-round';  Color = '#FFFFFF' }
)
foreach ($p in $products) {
    Write-Art $p.Art $p.Color '#F4F2FB' (Join-Path $wwwroot "images/products/$($p.Slug).svg")
}

Write-Art 'tshirt' '#FFFFFF' '#EDE9FE' (Join-Path $wwwroot 'images/categories/t-shirts.svg')
Write-Art 'mug' '#FFFFFF' '#CFFAFE' (Join-Path $wwwroot 'images/categories/mugs.svg')
Write-Art 'keychain-round' '#FFFFFF' '#FCE7F3' (Join-Path $wwwroot 'images/categories/keychains.svg')
Write-Output "Generated $($products.Count) product and 3 category images."
