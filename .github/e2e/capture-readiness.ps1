# Test-only readiness helpers. They do not operate browsers or change the application.
# The deadline bounds polling/sleeps, not the duration of a blocking UIA/filesystem read.
function Wait-CaptureTextObservation {
  param(
    [Parameter(Mandatory)][scriptblock]$Read,
    [Parameter(Mandatory)][scriptblock]$Accept,
    [ValidateRange(1,60000)][int]$TimeoutMilliseconds = 5000,
    [ValidateRange(1,1000)][int]$PollMilliseconds = 100
  )
  $clock = [Diagnostics.Stopwatch]::StartNew()
  $last = ''; $attempts = 0; $lastError = ''
  do {
    $attempts++
    try {
      $last = [string](& $Read)
      $lastError = ''
      $accepted = $false
      if ($clock.ElapsedMilliseconds -le $TimeoutMilliseconds) { $accepted = [bool](& $Accept $last) }
      if ($accepted -and $clock.ElapsedMilliseconds -le $TimeoutMilliseconds) {
        return [pscustomobject]@{ Matched = $true; Value = $last; Attempts = $attempts; ElapsedMilliseconds = $clock.ElapsedMilliseconds; ReadErrorType = '' }
      }
    } catch { $lastError = $_.Exception.GetType().Name }
    $remaining = $TimeoutMilliseconds - $clock.ElapsedMilliseconds
    if ($remaining -le 0) { break }
    Start-Sleep -Milliseconds ([int][Math]::Min($PollMilliseconds, $remaining))
  } while ($clock.ElapsedMilliseconds -lt $TimeoutMilliseconds)
  return [pscustomobject]@{ Matched = $false; Value = $last; Attempts = $attempts; ElapsedMilliseconds = $clock.ElapsedMilliseconds; ReadErrorType = $lastError }
}

function Test-CaptureSavedInstance {
  param([string]$Json, [string]$WorkspaceName, [string]$Target, [string]$Name, [string]$Description, [string]$OpenWith)
  try {
    $state = $Json | ConvertFrom-Json -ErrorAction Stop
    if ($null -eq $state) { return $false }
    $workspace = @($state.workspaces | Where-Object { $_.name -ceq $WorkspaceName })
    $item = @($state.items.PSObject.Properties.Value | Where-Object { $_.target -ceq $Target })
    if ($workspace.Count -ne 1 -or $item.Count -ne 1) { return $false }
    $members = @($workspace[0].items | Where-Object { $_.itemId -ceq $item[0].id })
    if ($members.Count -ne 1) { return $false }
    $member = $members[0]
    return ($member.name -ceq $Name -and $member.description -ceq $Description -and $member.launch.openWith -ceq $OpenWith)
  } catch { return $false }
}
