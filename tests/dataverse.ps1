<#
    Web API access to the environment, for the parts of the fixture no solution can carry.

    An unmanaged registration is not a solution. There is nothing to import and nothing to
    delete afterwards, so the records have to be written and removed one at a time - and
    pac, which every other script here talks to, only reads. This is the smallest thing
    that closes that gap: an access token and four verbs.

    Dot-source it: . (Join-Path $PSScriptRoot 'dataverse.ps1')

    Authentication is pac's. The Power Platform CLI keeps its MSAL token cache beside its
    auth profiles, DPAPI protected for the current user, and it holds an access token for
    whichever organization it last talked to. Reading that is not a second way to sign in;
    it is the same sign in the rest of the suite already relies on, which is why these
    scripts still need nothing but a working `pac auth`. When the cached token has expired
    pac is asked to talk to the environment, which makes it refresh, and the cache is read
    again. The refresh token is never touched - redeeming it here would rotate it out from
    under pac and leave the profile broken.
#>

Set-StrictMode -Version Latest

$script:DataverseUrl = $null
$script:DataverseToken = $null
$script:DataverseTokenExpires = [DateTimeOffset]::MinValue

# Everything else in the suite passes --environment straight through to pac and never has
# to know where it points. The Web API does, so ask pac to say it out loud.
function Connect-Dataverse {
    param([string] $Environment)

    $envArgs = if ($Environment) { @('--environment', $Environment) } else { @() }
    $script:DataverseEnvArgs = $envArgs

    $output = & pac @(@('env', 'who') + $envArgs) 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        throw "pac env who failed. Is there an active auth profile?`r`n$($output -join "`r`n")"
    }

    $line = $output | Where-Object { $_ -match 'Org URL:\s+(\S+)' }
    if (-not $line) {
        throw "Could not read the organization URL out of pac env who:`r`n$($output -join "`r`n")"
    }

    [void]($line -match 'Org URL:\s+(\S+)')
    $script:DataverseUrl = $Matches[1].TrimEnd('/')
    $script:DataverseUrl
}

function Get-DataverseToken {
    $path = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerAppsCli\tokencache_msalv3.dat'
    if (-not (Test-Path $path)) {
        throw "The Power Platform CLI token cache is not at $path. Run 'pac auth create' first."
    }

    if (-not $script:DataverseUrl) {
        throw 'Call Connect-Dataverse before Get-DataverseToken.'
    }

    $organization = ([Uri]$script:DataverseUrl).Host

    # The cache is a bag of tokens for every resource pac has ever been asked about; the
    # one wanted is whichever was issued for this organization.
    function Read-Cache {
        Add-Type -AssemblyName System.Security
        $bytes = [IO.File]::ReadAllBytes($path)
        $plain = [Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, 'CurrentUser')
        $cache = [Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json

        foreach ($entry in $cache.AccessToken.PSObject.Properties) {
            if ($entry.Value.target -like "*$organization*") {
                return [pscustomobject]@{
                    Token   = $entry.Value.secret
                    Expires = [DateTimeOffset]::FromUnixTimeSeconds([long]$entry.Value.expires_on)
                }
            }
        }
    }

    # A minute of headroom, so a token cannot expire between here and the request.
    $cutoff = [DateTimeOffset]::UtcNow.AddMinutes(1)
    if ($script:DataverseToken -and $script:DataverseTokenExpires -gt $cutoff) {
        return $script:DataverseToken
    }

    $cached = Read-Cache
    if (-not $cached -or $cached.Expires -le $cutoff) {
        # Make pac talk to the environment, which refreshes its own token into the cache.
        & pac @(@('env', 'who') + $script:DataverseEnvArgs) *> $null
        $cached = Read-Cache
    }

    if (-not $cached) {
        throw "No cached token for $organization. Run 'pac env who' and try again."
    }
    if ($cached.Expires -le $cutoff) {
        throw "The cached token for $organization expired at $($cached.Expires.LocalDateTime) and pac did not renew it. Run 'pac auth create --environment $script:DataverseUrl'."
    }

    $script:DataverseToken = $cached.Token
    $script:DataverseTokenExpires = $cached.Expires
    $cached.Token
}

<#
    One request. Path is relative to the Web API root, so 'pluginassemblies' or
    'sdkmessageprocessingsteps(<id>)'.

    Dataverse answers a failure with a JSON body that says what was wrong and Invoke-
    RestMethod throws away everything but the status line, so errors are read out of the
    response here instead: a bare "Bad Request" from any of this is unusable.
#>
function Invoke-Dataverse {
    param(
        [ValidateSet('GET', 'POST', 'PATCH', 'DELETE')] [string] $Method,
        [Parameter(Mandatory)] [string] $Path,
        [hashtable] $Body
    )

    $headers = @{
        'Authorization'    = "Bearer $(Get-DataverseToken)"
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Accept'           = 'application/json'
    }

    $arguments = @{
        Uri                = "$script:DataverseUrl/api/data/v9.2/$Path"
        Method             = $Method
        Headers            = $headers
        SkipHttpErrorCheck = $true
    }

    if ($PSBoundParameters.ContainsKey('Body')) {
        $arguments.Body = ($Body | ConvertTo-Json -Depth 5 -Compress)
        $arguments.ContentType = 'application/json; charset=utf-8'
    }

    $response = Invoke-WebRequest @arguments
    if ($response.StatusCode -ge 400) {
        $message = $response.Content
        try { $message = ($response.Content | ConvertFrom-Json).error.message } catch { }
        throw "$Method $Path failed ($($response.StatusCode)): $message"
    }

    if ($response.Content) {
        $response.Content | ConvertFrom-Json
    }
}

<#
    Writes a record at an id of the fixture's own choosing.

    A PATCH to a record that is not there creates it, which is what keeps register.ps1 as
    safe to re-run as a solution import is: the same call registers the fixture the first
    time and brings it back into line every time after. The id is the address, so it is
    not repeated in the body.
#>
function Set-DataverseRecord {
    param(
        [Parameter(Mandatory)] [string] $Set,
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [hashtable] $Body
    )

    Invoke-Dataverse -Method PATCH -Path "$Set($Id)" -Body $Body | Out-Null
}

<#
    Deletes a record, and says so, treating "it was not there" as done rather than as a
    failure - unregister.ps1 has to be runnable at any time, including twice.
#>
function Remove-DataverseRecord {
    param([Parameter(Mandatory)] [string] $Set, [Parameter(Mandatory)] [string] $Id)

    $response = Invoke-WebRequest -Uri "$script:DataverseUrl/api/data/v9.2/$Set($Id)" `
        -Method DELETE -SkipHttpErrorCheck -Headers @{
            'Authorization' = "Bearer $(Get-DataverseToken)"
            'Accept'        = 'application/json'
        }

    if ($response.StatusCode -eq 404) {
        return $false
    }
    if ($response.StatusCode -ge 400) {
        $message = $response.Content
        try { $message = ($response.Content | ConvertFrom-Json).error.message } catch { }
        throw "Deleting $Set($Id) failed ($($response.StatusCode)): $message"
    }

    $true
}

<#
    Returns the rows of a query, or an empty array. The Web API answers a query with an
    object wrapping a value array, and PowerShell unrolls a one row array back into the row
    itself, so both are flattened here rather than at every call site.
#>
function Get-DataverseRecords {
    param([Parameter(Mandatory)] [string] $Query)

    $response = Invoke-Dataverse -Method GET -Path $Query
    , @($response.value)
}
