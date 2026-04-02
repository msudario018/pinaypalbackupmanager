# Generate random invite code
$chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
$code = ""
for ($i = 0; $i -lt 8; $i++) {
    $random = Get-Random -Maximum $chars.Length
    $code += $chars[$random]
}

Write-Host "New invite code: $code"
Write-Host ""
Write-Host "Update this in Firebase:"
Write-Host "1. Go to: https://pinaypal-backup-manager-default-rtdb.firebaseio.com/"
Write-Host "2. Click inviteCodes → current"
Write-Host "3. Replace with: $code"
Write-Host ""
Write-Host "Or use it in the app settings"
Read-Host "Press Enter to exit"
