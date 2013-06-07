param(
    $tasks = "Default",
	$product = "Rinky",
    $version
)


#========================================
# Ensure we have required params
#========================================

while (-not $product) { $product = Read-Host "Product" }
while (-not $tasks) { $tasks = Read-Host "Tasks" }
while (-not $version) { $version = Read-Host "Version" }


#========================================
# Run the build
#========================================

$base_dir = Split-Path $MyInvocation.MyCommand.Definition
Import-Module $base_dir\tools\psake\psake.psm1 -Force
Invoke-psake $base_dir\default.ps1 $tasks -properties @{ "version" = $version; "product" = $product }

if (-not $psake.build_success) {
    Write-Host "Error during build!" -ForegroundColor Red
}