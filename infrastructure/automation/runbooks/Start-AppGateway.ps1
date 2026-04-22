param(
  [string]$SubscriptionId,
  [string]$ResourceGroup,
  [string]$GatewayName,
  [string]$BusinessTimeZoneId = 'Romance Standard Time'
)

$localNow = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId((Get-Date), $BusinessTimeZoneId)
if ($localNow.DayOfWeek -eq [System.DayOfWeek]::Saturday -or $localNow.DayOfWeek -eq [System.DayOfWeek]::Sunday) {
  Write-Output "Weekend in $BusinessTimeZoneId ($($localNow.DayOfWeek)); skipping start."
  return
}

Connect-AzAccount -Identity | Out-Null
Set-AzContext -SubscriptionId $SubscriptionId | Out-Null

$gateway = Get-AzApplicationGateway -Name $GatewayName -ResourceGroupName $ResourceGroup
if ($gateway.OperationalState -eq 'Running') {
  Write-Output "Application Gateway '$GatewayName' already running."
  return
}

Start-AzApplicationGateway -ApplicationGateway $gateway | Out-Null
Write-Output "Start requested for Application Gateway '$GatewayName'."
