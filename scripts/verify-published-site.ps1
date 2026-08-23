<#
.SYNOPSIS
    Fails if the published site is not serving the manifests that were just deployed.

.DESCRIPTION
    A GitHub Pages deployment can report success, be recorded as the active deployment,
    and still leave the site serving the previous one. That happened to 1.0.0: the
    release-triggered run waited for the release to appear, wrote the correct manifests,
    uploaded them, and deployed them, and every step and the deployment itself were
    green - while whiteboard.sqlbi.com kept offering 0.9.5 until the workflow was re-run
    by hand. Nothing upstream reports that, and the only symptom is a download page
    quietly a version behind.

    This compares what the site serves against the files the deployment just wrote,
    rather than against the newest release. That distinction matters: a pre-release
    deployment leaves stable.json untouched, which is correct, and a check written
    against the newest release would fail on every one of them.

    Read-only. It cannot repair a deployment - the remedy is to run the workflow again,
    which publishes the same files - so its whole job is to make a silent staleness loud.

.PARAMETER Folder
    Folder holding the manifests that were deployed, and the CNAME naming where they were
    deployed to.

.PARAMETER BaseUrl
    Origin to read the manifests back from. Defaults to the CNAME in -Folder, so the check
    follows the domain the deployment itself carries rather than repeating it here.

.PARAMETER TimeoutSeconds
    How long to keep asking before failing. Generous on purpose: a slow propagation and a
    stuck deployment look identical at first, and only one of them is worth interrupting a
    release for.

.PARAMETER IntervalSeconds
    Delay between attempts.
#>
[CmdletBinding()]
param(
    [string]$Folder = 'site',
    [string]$BaseUrl,
    [int]$TimeoutSeconds = 300,
    [int]$IntervalSeconds = 15
)

$ErrorActionPreference = 'Stop'

if (-not $BaseUrl) {
    $cnamePath = Join-Path $Folder 'CNAME'
    if (-not (Test-Path $cnamePath)) {
        throw "No -BaseUrl was given and there is no CNAME in '$Folder' to read it from."
    }

    $BaseUrl = "https://$((Get-Content $cnamePath -Raw).Trim())"
}

$expected = [ordered]@{}
foreach ($name in 'stable.json', 'dev.json') {
    $path = Join-Path $Folder $name
    if (Test-Path $path) {
        $expected[$name] = (Get-Content $path -Raw | ConvertFrom-Json).version
    }
}

if ($expected.Count -eq 0) {
    Write-Host "No manifests were deployed, so there is nothing to verify."
    return
}

# Both a fresh query string and the no-cache headers, because the two caches in front of
# Pages do not honour the same things, and a stale read here would report the failure this
# script exists to catch.
$headers = @{ 'Cache-Control' = 'no-cache'; 'Pragma' = 'no-cache' }
$pending = [System.Collections.Generic.List[string]]::new()
$expected.Keys | ForEach-Object { $pending.Add($_) }
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

while ($true) {
    foreach ($name in @($pending)) {
        $want = $expected[$name]
        $url = "$BaseUrl/${name}?probe=$([Guid]::NewGuid().ToString('N'))"
        $served = $null
        try {
            $served = (Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 30).version
        }
        catch {
            $served = "unreachable ($($_.Exception.Message))"
        }

        if ($served -eq $want) {
            Write-Host "$name is serving $want."
            [void]$pending.Remove($name)
        }
        else {
            Write-Host "$name is serving '$served', waiting for '$want'."
        }
    }

    if ($pending.Count -eq 0) {
        Write-Host "The site is serving what this run deployed."
        return
    }

    if ([DateTime]::UtcNow -ge $deadline) {
        break
    }

    Start-Sleep -Seconds $IntervalSeconds
}

$stale = ($pending | ForEach-Object { "$_ should be $($expected[$_])" }) -join '; '
throw @"
$BaseUrl is still serving an older deployment after $TimeoutSeconds seconds ($stale).

The deployment this run made succeeded, so the files are right and the site has not
picked them up. Run this workflow again - Actions -> Publish site -> Run workflow - and
it will publish the same files. That has been enough every time so far.
"@
