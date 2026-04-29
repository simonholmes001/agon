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

Invoke-AzRestMethod `
  -Method POST `
  -Path "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Network/applicationGateways/$GatewayName/start?api-version=2023-09-01" | Out-Null
Write-Output "Start requested for Application Gateway '$GatewayName'."
