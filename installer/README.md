# Installer

The current v0.1 distribution path is:

```powershell
.\scripts\publish.ps1
.\scripts\install.ps1
```

`publish.ps1` creates a self-contained `artifacts\AirPlaySender-win-x64.zip`.
`install.ps1` copies it to `%LOCALAPPDATA%\Programs\AirPlaySender`, creates a
Start Menu shortcut, and optionally adds the inbound UDP firewall rule when run
elevated.

A WiX MSI is planned for the next packaging phase. It should:

- install per-machine or per-user with a clean uninstall;
- add the inbound UDP firewall rule;
- optionally register a startup entry;
- ship the GPL/Apache notices and a source offer;
- be signed with an Authenticode certificate.
