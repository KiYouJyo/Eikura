param()
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Eikura-store-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$package = Join-Path $testRoot 'Eikura_1.3.4.0_x64.msixupload'
Set-Content $package 'mock package'
$names = @('AZURE_AD_TENANT_ID','AZURE_AD_APPLICATION_CLIENT_ID','AZURE_AD_APPLICATION_SECRET','SELLER_ID','GITHUB_STEP_SUMMARY')
$original = @{}
foreach ($name in $names) { $original[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, 'test-placeholder') }
[Environment]::SetEnvironmentVariable('GITHUB_STEP_SUMMARY', '')
$global:storePublicationTestcalls = [Collections.Generic.List[string]]::new()
$global:storePublicationTestscenario = 'success'
$global:storePublicationTestuploaded = $false
$global:storePublicationTestupdatedSubmission = $null
function msstore {
    $global:storePublicationTestcalls.Add('cli:' + $args[0])
    if ($args[0] -eq 'publish') { $global:storePublicationTestuploaded = $true }
    $global:LASTEXITCODE = 0
}
function Invoke-RestMethod {
    param($Method, $Uri, $Body, $ContentType, $Headers, $TimeoutSec)
    $global:storePublicationTestcalls.Add("$Method $(([Uri]$Uri).AbsolutePath)")
    if ($Uri -like '*oauth2/token') { return @{access_token='mock-token'} }
    if (([Uri]$Uri).AbsolutePath -eq '/v1.0/my/applications') {
        $apps = @(@{id='TESTAPP';packageFamilyName='JoKiy.Eizo_4wdwgytaw3v2m';packageIdentityName='JoKiy.Eizo'})
        if ($global:storePublicationTestscenario -eq 'ambiguous') { $apps += $apps[0] }
        return @{value=$apps}
    }
    if ($Uri -like '*/applications/TESTAPP') {
        $pending = if ($global:storePublicationTestuploaded -or $global:storePublicationTestscenario -eq 'pending') { @{id='123'} } else { $null }
        return [pscustomobject]@{pendingApplicationSubmission=$pending}
    }
    if ($Uri -like '*/status') { return @{status='Certification'} }
    if ($Uri -like '*/commit') { return @{} }
    if ($Method -eq 'Put') { $global:storePublicationTestupdatedSubmission = $Body | ConvertFrom-Json; return @{} }
    return [pscustomobject]@{status='PendingCommit';targetPublishMode='Manual';pricing=@{priceId='Free'};listings=[pscustomobject]@{'zh-cn'=[pscustomobject]@{baseListing=[pscustomobject]@{releaseNotes='old';description='preserve'}}}}
}
try {
    & (Join-Path $PSScriptRoot 'Publish-EikuraMicrosoftStore.ps1') -PackagePath $package
    if (-not ($global:storePublicationTestcalls | Where-Object { $_ -eq 'Post /v1.0/my/applications/TESTAPP/submissions/123/commit' })) { throw 'Submission was not committed.' }
    if ($global:storePublicationTestupdatedSubmission.targetPublishMode -ne 'Immediate' -or $global:storePublicationTestupdatedSubmission.listings.'zh-cn'.baseListing.description -ne 'preserve' -or $global:storePublicationTestupdatedSubmission.pricing.priceId -ne 'Free') { throw 'Publish mode or metadata preservation failed.' }
    foreach ($scenario in @('ambiguous','pending')) {
        $global:storePublicationTestscenario=$scenario; $global:storePublicationTestuploaded=$false; $global:storePublicationTestcalls.Clear()
        $rejected=$false
        try { & (Join-Path $PSScriptRoot 'Publish-EikuraMicrosoftStore.ps1') -PackagePath $package } catch { $rejected=$true }
        if (-not $rejected -or ($global:storePublicationTestcalls | Where-Object { $_ -like 'cli:*' -or $_ -like 'Put *' -or $_ -like 'Post *submissions*' })) { throw "Unsafe scenario was not stopped: $scenario" }
    }
    Write-Host 'Microsoft Store publication tests PASS: commit, Immediate mode, metadata preservation, ambiguous identity, pending submission.'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$original[$name]) }
    Remove-Item -LiteralPath $package -Force
    Remove-Item -LiteralPath $testRoot -Force
}
