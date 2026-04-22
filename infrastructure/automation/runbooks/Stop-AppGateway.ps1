param(
  [string]$SubscriptionId,
  [string]$ResourceGroup,
  [string]$GatewayName,
  [string]$BusinessTimeZoneId = 'Romance Standard Time'
)

$localNow = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId((Get-Date), $BusinessTimeZoneId)
if ($localNow.DayOfWeek -eq [System.DayOfWeek]::Saturday -or $localNow.DayOfWeek -eq [System.DayOfWeek]::Sunday) {
  Write-Output "Weekend in $BusinessTimeZoneId ($($localNow.DayOfWeek)); skipping stop."
  return
}

Connect-AzAccount -Identity | Out-Null
Set-AzContext -SubscriptionId $SubscriptionId | Out-Null

$gateway = Get-AzApplicationGateway -Name $GatewayName -ResourceGroupName $ResourceGroup
if ($gateway.OperationalState -eq 'Stopped') {
  Write-Output "Application Gateway '$GatewayName' already stopped."
  return
}

Stop-AzApplicationGateway -ApplicationGateway $gateway | Out-Null
Write-Output "Stop requested for Application Gateway '$GatewayName'."
