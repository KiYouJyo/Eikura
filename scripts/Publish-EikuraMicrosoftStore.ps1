param([string]$PackagePath, [switch]$InspectOnly, [switch]$Resume)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$identity = Get-Content (Join-Path $repoRoot 'release/MicrosoftStore/store-identity.json') -Raw | ConvertFrom-Json
$release = Get-Content (Join-Path $repoRoot 'release/release.json') -Raw | ConvertFrom-Json
if (-not $InspectOnly -and -not $Resume -and (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf) -or
    (Split-Path $PackagePath -Leaf) -cne "Eikura_$($release.product.packageVersion)_x64.msixupload")) {
    throw 'Expected verified StoreUpload package is missing.'
}
foreach ($name in @('AZURE_AD_TENANT_ID','AZURE_AD_APPLICATION_CLIENT_ID','AZURE_AD_APPLICATION_SECRET','SELLER_ID')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Required GitHub secret is missing: $name" }
}
# Credentials, access tokens and SAS URLs stay on the ephemeral Actions runner.
# Never serialize API responses or CLI output into logs/artifacts.
try {
    $auth = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$($env:AZURE_AD_TENANT_ID)/oauth2/token" -Body @{
        grant_type = 'client_credentials'
        client_id = $env:AZURE_AD_APPLICATION_CLIENT_ID
        client_secret = $env:AZURE_AD_APPLICATION_SECRET
        resource = 'https://manage.devcenter.microsoft.com'
    } -ContentType 'application/x-www-form-urlencoded'
} catch { throw 'Microsoft Store authentication failed; inspect Entra application permissions and secret expiry.' }
$headers = @{ Authorization = "Bearer $($auth.access_token)" }
$apiRoot = 'https://manage.devcenter.microsoft.com/v1.0/my/'
function Invoke-StoreApi([string]$Method, [string]$Path, $Body = $null) {
    try {
        $request = @{ Method = $Method; Uri = "$apiRoot$Path"; Headers = $headers; TimeoutSec = 120 }
        if ($null -ne $Body) { $request.Body = $Body | ConvertTo-Json -Depth 100 -Compress; $request.ContentType = 'application/json; charset=utf-8' }
        return Invoke-RestMethod @request
    } catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'network' }
        $detail = [string]$_.ErrorDetails.Message
        foreach ($name in @('AZURE_AD_APPLICATION_SECRET','AZURE_AD_APPLICATION_CLIENT_ID','AZURE_AD_TENANT_ID','SELLER_ID')) {
            $value = [Environment]::GetEnvironmentVariable($name)
            if ($value) { $detail = $detail.Replace($value, '[redacted]') }
        }
        $detail = $detail.Replace([string]$auth.access_token, '[redacted]')
        $detail = [regex]::Replace($detail, 'https?://[^\s"<>]+', '[redacted-url]')
        throw "Microsoft Store API $Method failed ($status): $detail"
    }
}
$apps = @()
$next = 'applications?top=100'
do {
    $page = Invoke-StoreApi 'Get' $next
    $apps += @($page.value)
    $next = if ($page.PSObject.Properties['@nextLink']) { [string]$page.'@nextLink' } else { '' }
    if ($next -and $next -notmatch '^applications\?') { throw 'Unexpected Store API pagination path.' }
} while ($next)
$matches = @($apps | Where-Object { $_.packageFamilyName -ceq $identity.packageFamilyName -and $_.packageIdentityName -ceq $identity.packageIdentityName })
if ($matches.Count -ne 1) { throw 'Exactly one Store application must match the production package identity.' }
$appId = [string]$matches[0].id
$app = Invoke-StoreApi 'Get' "applications/$appId"
if ($InspectOnly) {
    Write-Host "Store product: $appId"
    if ($app.PSObject.Properties['pendingApplicationSubmission'] -and $null -ne $app.pendingApplicationSubmission) {
        $id = [string]$app.pendingApplicationSubmission.id
        $pending = Invoke-StoreApi 'Get' "applications/$appId/submissions/$id"
        Write-Host "Pending submission: $id; status=$($pending.status); publishMode=$($pending.targetPublishMode)"
        foreach ($package in $pending.applicationPackages) {
            Write-Host "Package: $($package.fileName); fileStatus=$($package.fileStatus)"
        }
        foreach ($error in $pending.statusDetails.errors) { Write-Host "Store validation error code: $($error.code)" }
    } else { Write-Host 'No pending submission.' }
    exit 0
}
if (-not $Resume -and $app.PSObject.Properties['pendingApplicationSubmission'] -and $null -ne $app.pendingApplicationSubmission) {
    throw "Store app $appId already has a pending submission; it has been preserved."
}
Write-Host "Resolved Store product $appId for $($identity.packageFamilyName)."
$cliOutput = & msstore reconfigure --tenantId $env:AZURE_AD_TENANT_ID --sellerId $env:SELLER_ID --clientId $env:AZURE_AD_APPLICATION_CLIENT_ID --clientSecret $env:AZURE_AD_APPLICATION_SECRET 2>&1
if ($LASTEXITCODE -ne 0) { throw "Microsoft Store CLI configuration failed ($LASTEXITCODE)." }
$cliOutput = $null
if (-not $Resume) {
    $cliOutput = & msstore publish $PackagePath -id $appId --noCommit 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Microsoft Store package upload failed ($LASTEXITCODE); CLI output suppressed to protect upload credentials." }
    $cliOutput = $null
    $app = Invoke-StoreApi 'Get' "applications/$appId"
}
$submissionId = [string]$app.pendingApplicationSubmission.id
if ([string]::IsNullOrWhiteSpace($submissionId)) { throw 'Package upload did not create a pending submission.' }
$path = "applications/$appId/submissions/$submissionId"
$submission = Invoke-StoreApi 'Get' $path
if ($submission.status -ne 'PendingCommit') { throw "Unexpected submission status: $($submission.status)" }
if ($Resume) {
    $expectedFile = "Eikura_$($release.product.packageVersion)_x64.msixupload"
    $currentPackages = @($submission.applicationPackages | Where-Object { $_.fileStatus -ne 'PendingDelete' })
    if ($currentPackages.Count -ne 1 -or $currentPackages[0].fileName -cne $expectedFile -or $submission.targetPublishMode -ne 'Immediate') {
        throw 'Resume is limited to the matching Eikura release draft with Immediate publication.'
    }
}
$submission.targetPublishMode = 'Immediate'
foreach ($listing in $submission.listings.PSObject.Properties) {
    $suffix = if ($listing.Name -like 'zh-*') { '' } elseif ($listing.Name -like 'ja-*') { '.ja' } else { '.en' }
    $notes = [IO.File]::ReadAllText((Join-Path $repoRoot "docs/RELEASE-NOTES-v$($release.product.version)$suffix.md"))
    if ($Resume -and $listing.Value.baseListing.releaseNotes -cne $notes) { throw 'Resume draft release notes do not match this release.' }
    $listing.Value.baseListing.releaseNotes = $notes
}
if (-not $Resume) { $null = Invoke-StoreApi 'Put' $path $submission }
Write-Host "Submitting Store draft $submissionId."
$cliOutput = & msstore submission publish $appId 2>&1
if ($LASTEXITCODE -ne 0) {
    $detail = ($cliOutput | Select-Object -Last 12 | Out-String)
    foreach ($name in @('AZURE_AD_APPLICATION_SECRET','AZURE_AD_APPLICATION_CLIENT_ID','AZURE_AD_TENANT_ID','SELLER_ID')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { $detail = $detail.Replace($value, '[redacted]') }
    }
    $detail = [regex]::Replace($detail, 'https?://[^\s"<>]+|Bearer\s+[^\s"<>]+', '[redacted]')
    throw "Store submission commit failed ($LASTEXITCODE): $detail"
}
$cliOutput = $null
Write-Host "Committed Microsoft Store submission $submissionId for Eikura $($release.product.version)."
# Wait for ingestion to acknowledge the committed package. Certification may
# continue after this workflow; do not label an in-review version as published.
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    $status = Invoke-StoreApi 'Get' "$path/status"
    Write-Host "Store submission status: $($status.status)"
    if ($status.status -in @('CommitFailed','PreProcessingFailed','CertificationFailed','ReleaseFailed','PublishFailed','Canceled')) {
        $codes = @($status.statusDetails.errors | ForEach-Object code) -join ', '
        throw "Store submission failed: $($status.status); codes=$codes"
    }
    if ($status.status -in @('PreProcessing','Certification','Release','Publishing','Published')) {
        $summary = "Eikura $($release.product.version): Store product $appId, submission $submissionId, status $($status.status), publish mode Immediate."
        Write-Host $summary
        if ($env:GITHUB_STEP_SUMMARY) { $summary | Out-File $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8 }
        exit 0
    }
    Start-Sleep -Seconds 15
}
throw "Store submission $submissionId was committed but ingestion acknowledgement timed out. Check status before rerunning."
