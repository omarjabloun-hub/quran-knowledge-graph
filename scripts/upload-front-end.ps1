# Builds the front end (C# -> JavaScript with Transpose) and uploads it. Touches nothing else.
#
# Usage: scripts/upload-front-end.ps1 [--target local|prod]
. (Join-Path $PSScriptRoot "lib.ps1")

Import-DotEnv
$target = Get-Target $args
Resolve-Target $target
Assert-Command dotnet        "Install the .NET 10 SDK."
Assert-Command tps           "dotnet tool install --global Transpose.Compiler"
Assert-Command curiosity-cli "dotnet tool install --global Curiosity.CLI"
Assert-UploadToken $target
Assert-WorkspaceReachable

Write-Step "Building the front end"
Invoke-Checked "front-end build" { dotnet build $script:FrontEndCsproj -c Release }

Write-Step "Uploading to $env:CURIOSITY_URL  [target: $target]"
Invoke-Checked "upload" { curiosity-cli upload-front-end -s $env:CURIOSITY_URL -t $env:INTERFACE_UPLOAD_TOKEN -p $script:FrontEndOutput }

Write-Step "Done"
Write-Info "Reload the workspace in the browser (a hard reload if the old bundle is cached)."
