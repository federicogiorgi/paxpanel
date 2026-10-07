#Requires -RunAsAdministrator
# Stops paxpanel and removes its logon task. Leaves the publish folder and logs in place.
Get-Process PaxPanel -ErrorAction SilentlyContinue | Stop-Process -Force
Unregister-ScheduledTask -TaskName 'paxpanel' -Confirm:$false -ErrorAction SilentlyContinue
'paxpanel stopped and its logon task removed.'
"Logs remain in $env:LOCALAPPDATA\paxpanel; the publish folder was left in place."
