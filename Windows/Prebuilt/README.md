# BitReaper for Windows: ready-to-run executable

[`BitReaper.exe`](BitReaper.exe) runs on Windows 10 and 11 (x64) with nothing else to install: no .NET, no build.
To get it without cloning the repo, open the file on GitHub and click **Download raw file**.

1. Run `BitReaper.exe`. It isn't code-signed, so the first time SmartScreen says "Windows protected your PC":
   click **More info**, then **Run anyway**.
2. To keep it, open **Settings** (the gear, or Ctrl+,) and click **Install BitReaper**. It installs for your user
   only: a Start menu shortcut, **Scan with BitReaper** when you right-click a folder or drive in Explorer, and an
   entry in **Settings › Apps** to uninstall it.
3. For complete results, click **Restart as Administrator** on the home screen.

| | |
| --- | --- |
| Version | 1.0.0 |
| Built from | the Windows sources in [pull request #1](https://github.com/Pau-mbox/BitReaper/pull/1), branch `windows-support` (commit `51b6571`) |
| Build | `Windows\Scripts\build.ps1` (self-contained single file, win-x64) |
| Size | 68,753,089 bytes |
| SHA-256 | `5cda380e3b07c5e032412912a8cc59ed3417eddf6d34fbef1c82e3353d935238` |

Check the download in PowerShell: `Get-FileHash BitReaper.exe`.
