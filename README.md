<div align="center">

<img src="docs/images/icon.png" width="128" alt="BitReaper icon">

<img src="docs/images/logo.png" width="520" alt="BitReaper">

**See what's filling your disk. Collect what you don't need. Reap it in one go.**

A native disk-space analyzer for macOS and Windows with a fast tree view, a drag-and-drop collector and a synthwave soul.

![macOS 14+](https://img.shields.io/badge/macOS-14%2B-a24bff?style=flat-square&logo=apple&logoColor=white)
![Windows 10 & 11](https://img.shields.io/badge/Windows-10%20%26%2011-2f8bff?style=flat-square&logo=windows&logoColor=white)
![Swift](https://img.shields.io/badge/Swift-SwiftUI%20%2B%20AppKit-ff2bd6?style=flat-square&logo=swift&logoColor=white)
![Apple Silicon & Intel](https://img.shields.io/badge/Apple%20Silicon%20%26%20Intel-native-22e4f5?style=flat-square)
![License: MIT](https://img.shields.io/badge/license-MIT-ffc93c?style=flat-square)

<img src="docs/images/demo.gif" width="760" alt="BitReaper in action">

<sub>🎬 <a href="docs/bitreaper-demo.mp4">Watch the 20-second demo in 1080p</a> · ⬇️ <a href="https://github.com/Pau-mbox/BitReaper/releases/latest/download/BitReaper-macOS.zip">Download for macOS</a> · ⬇️ <a href="https://github.com/Pau-mbox/BitReaper/releases/latest/download/BitReaper-Windows-x64.zip">Download for Windows</a></sub>

</div>

---

## Why BitReaper?

Your disk is full again and Finder isn't telling you why. BitReaper scans a disk or folder in seconds and shows
everything as a tree sorted by size, so the space hogs float straight to the top. Then you **collect** what you
want gone, review it, and delete it all at once. Nothing is touched until you say so.

It's built for the keyboard: arrow through folders, press ⌫ to collect, Space to preview.

<img src="docs/images/results.jpg" alt="The tree view with collected items, the donut chart and the collector">

## Features

### 🔍 Scan fast, measure right
- **Parallel scanner** built on `readdir`/`fstatat`: around a million files in roughly 7 seconds on an SSD.
- **Real sizes:** physical (allocated) space, matching `du` to the byte. Hard-linked files are counted once.
- Every mounted **disk** is listed with a live usage gauge; scan one, or any **folder** (⌘O, or drag it in).
- Other volumes are skipped, and the startup disk's APFS firmlinks are handled, so nothing is counted twice.
- **Hidden space**, **free + purgeable** space and local **Time Machine snapshots**, explained.

### 🌲 Explore
- A native tree with **Size** (glowing bar), **%**, **Items** and **Modified** columns, sortable by any of them.
- Each top-level item gets its own neon colour, inherited by everything inside it.
- **Breadcrumbs**, Back/Forward, zoom into any folder, and jump up a level.
- **Largest Files** view and instant **name search**, both scoped to the current folder.
- A sidebar **donut chart** of the folder, plus details for the selection.
- **Quick Look** with Space, Reveal in Finder, Copy Path, Rescan Folder.

### 🧺 Collect
- Press **⌫**, click **⊕** on any row, use the context menu, or **drag** items onto the collector.
- Collecting a folder absorbs anything already collected inside it, so nothing is counted twice.
- Review the collected list, Quick Look items, jump to them in the tree, or remove one with ✕.
- Essential folders (`/System`, `~/Library`, your home folder, whole disks…) **can't be collected**.

### 💀 Reap
- One **Delete** button for everything you've collected, with a review sheet first.
- Choose **Move to Trash** (the default, with **Put Back** right after) or **Delete Immediately**.
- A **5-second safety countdown** you can cancel with Esc.
- Read-only and locked files are handled. Anything that can't be deleted is listed and stays collected.

<table>
<tr>
<td><img src="docs/images/home.jpg" alt="Disks and folders"></td>
<td><img src="docs/images/scanning.jpg" alt="Scanning"></td>
</tr>
<tr>
<td align="center"><sub>Disks and Folders</sub></td>
<td align="center"><sub>Scanning</sub></td>
</tr>
<tr>
<td><img src="docs/images/largest.jpg" alt="Largest files"></td>
<td><img src="docs/images/results.jpg" alt="Collector"></td>
</tr>
<tr>
<td align="center"><sub>Largest Files</sub></td>
<td align="center"><sub>Collecting</sub></td>
</tr>
</table>

## Download

<a href="https://github.com/Pau-mbox/BitReaper/releases/latest/download/BitReaper-macOS.zip"><img src="https://img.shields.io/badge/Download-BitReaper%20for%20macOS-ff2bd6?style=for-the-badge&logo=apple&logoColor=white" alt="Download BitReaper for macOS"></a>

A universal app for Apple Silicon and Intel Macs, macOS 14 or later. See all versions on the
[Releases page](https://github.com/Pau-mbox/BitReaper/releases).

1. Unzip `BitReaper-macOS.zip` and drag **BitReaper** into your **Applications** folder.
2. Open it. BitReaper isn't notarized by Apple yet, so the first time macOS says it can't verify the app:
   - **macOS 15 Sequoia and later:** click **Done**, open **System Settings › Privacy & Security**, scroll down and
     click **Open Anyway** next to the BitReaper message, then confirm.
   - **macOS 14 Sonoma:** right-click the app, choose **Open**, then **Open** again.
   - Or, in Terminal: `xattr -dr com.apple.quarantine /Applications/BitReaper.app`
3. For complete results, add BitReaper in **System Settings › Privacy & Security › Full Disk Access**. Without
   it, macOS hides Mail, Messages, Safari and other apps' data, which then shows up as hidden space.

### Windows

<a href="https://github.com/Pau-mbox/BitReaper/releases/latest/download/BitReaper-Windows-x64.zip"><img src="https://img.shields.io/badge/Download-BitReaper%20for%20Windows-2f8bff?style=for-the-badge&logo=windows&logoColor=white" alt="Download BitReaper for Windows"></a>

One self-contained `BitReaper.exe` for Windows 10 and 11, nothing else to install. On Windows on ARM, download
[`BitReaper-Windows-arm64.zip`](https://github.com/Pau-mbox/BitReaper/releases/latest/download/BitReaper-Windows-arm64.zip)
instead. See all versions on the [Releases page](https://github.com/Pau-mbox/BitReaper/releases).

1. Unzip it and run `BitReaper.exe`. It isn't code-signed yet, so the first time SmartScreen says
   "Windows protected your PC": click **More info**, then **Run anyway**.
2. To keep it, open **Settings** (the gear, or Ctrl+,) and click **Install BitReaper**, or run
   `BitReaper.exe --install`. It installs for your user only, no administrator rights needed: a Start menu
   shortcut, **Scan with BitReaper** when you right-click a folder or drive in Explorer, and an entry in
   **Settings › Apps** to uninstall it.
3. For complete results, click **Restart as Administrator**. Without it, Windows hides other users' profiles,
   System Volume Information and parts of Windows and ProgramData, which then show up as hidden space.

The Windows app has the same tree, collector, countdown and Put Back as the Mac one, with Windows conventions:
deleted items go to the **Recycle Bin** (Windows asks first if an item is too big for it), junctions and symbolic
links are listed but never followed, sizes are what files really take on disk (compressed, sparse and
OneDrive cloud-only files included), and Windows, Program Files, your profile folders and drive roots can't be
collected. **Space** opens a preview window (images, text, audio and video), and **Properties** is in the
right-click menu.

## Build from source

### macOS

You need macOS 14 or later, Xcode 16+ and [XcodeGen](https://github.com/yonaskolb/XcodeGen).

```bash
brew install xcodegen
git clone https://github.com/Pau-mbox/BitReaper.git
cd BitReaper
Scripts/build.sh        # dist/BitReaper.app (Release, universal, ad-hoc signed)
Scripts/package.sh      # also zips it as dist/BitReaper-macOS.zip for a release
open dist/BitReaper.app
```

### Windows

You need Windows 10 or 11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/Pau-mbox/BitReaper.git
cd BitReaper
dotnet test Windows\BitReaper.Tests                 # scanner, collector and deleter tests (in a temp folder)
Windows\Scripts\build.ps1                          # dist\windows\x64\BitReaper.exe (self-contained, single file)
Windows\Scripts\package.ps1                        # also zips x64 and arm64 builds for a release
dist\windows\x64\BitReaper.exe
```

The [Windows workflow](.github/workflows/windows.yml) runs the tests on every push and pull request, and
attaches both zips to the release when a `v*` tag is pushed.

## Keyboard shortcuts

| Action | macOS | Windows |
| --- | --- | --- |
| Scan folder… | ⌘O | Ctrl+O |
| Disks and Folders | ⇧⌘D | Ctrl+Shift+D |
| Rescan folder | ⌘R | Ctrl+R or F5 |
| Find | ⌘F | Ctrl+F |
| Tree / Largest Files | ⌘1 / ⌘2 | Ctrl+1 / Ctrl+2 |
| Back / Forward | ⌘[ / ⌘] | Alt+← / Alt+→ |
| Enclosing folder / Zoom in | ⌘↑ / ⌘↓ (or Return, double-click) | Alt+↑ / Ctrl+↓ (or Enter, double-click) |
| Expand / collapse a folder | → / ← | → / ← |
| Quick Look / Preview | Space | Space |
| Add to / remove from Collector | ⌫ or ⌘⌫ | Del or Backspace |
| Show collected items | ⌘K | Ctrl+K |
| Delete collected items… | ⌥⌘⌫ | Ctrl+Shift+Del |
| Put back the last deletion | | Ctrl+Z |
| Cancel deletion countdown | Esc | Esc |
| Settings | ⌘, | Ctrl+, |

## How it works

| Piece | What it does |
| --- | --- |
| `Scanner` | Lists folders with `readdir` + `fstatat` and fans big subtrees out across cores; physical sizes, hard-link dedupe, mount-point exclusions. |
| `FileNode` | A compact tree (one small object per file) with sizes rolled up and children kept sorted. |
| `ScanSession` | Navigation, search and Largest Files (in the background), rescans, deletion with countdown and Put Back. |
| `Collector` | The staging area: no ancestor/descendant duplicates, protected-path checks. |
| `Deleter` | Moves to Trash or removes permanently, fixing read-only and locked items on the way. |
| UI | SwiftUI screens around an `NSOutlineView` tree, styled by `Theme.swift`. |

```
Sources/App      App entry, menus and shortcuts
Sources/Model    Scanner, FileNode, ScanSession, Collector, Deleter, volumes, preferences
Sources/Views    SwiftUI screens and the AppKit outline view
Sources/Util     Theme, palette, formatting, icons, Quick Look, system access
Resources        Info.plist and the asset catalog
Art/source       Original artwork
Scripts          build.sh · package.sh · process_art.py · make_video.py
Windows          The Windows app (C#, .NET 8, WPF): same model, same art, same synthwave look
```

The Windows app in `Windows/BitReaper` mirrors the macOS structure piece by piece (`Model/Scanner.cs`,
`Model/ScanSession.cs`, `Views/FileTree.cs`…). Its scanner lists each folder in bulk with
`GetFileInformationByHandleEx` (allocation sizes and file ids in one call), counts hard links once by file id on
NTFS and ReFS, and never follows junctions, symbolic links or mount points. Recycling goes through the shell,
and Put Back moves the item out of the Recycle Bin using its `$I` record. It reuses the artwork in
`Resources/Assets.xcassets`; `Windows/Scripts/make_icon.py` builds the `.ico` from the app icon.

### Rebuilding the art and the demo

```bash
python3 Scripts/process_art.py                              # icon and images → Assets.xcassets (Pillow, NumPy)
python3 Scripts/make_video.py <frames-dir> docs/bitreaper-demo.mp4 docs/images/demo.gif   # needs ffmpeg
```

`make_video.py` builds the promo from window screenshots of the app; its docstring lists the frames it expects.

## FAQ

**Is it safe?** Nothing is deleted until you click Delete, confirm, and let the countdown run out. Trash is the
default, so you can Put Back or restore from the Trash. Essential system and user folders can't be collected.

**Why does the disk show "hidden space"?** It's space the disk reports as used that the scan couldn't see:
other APFS volumes in the same container (macOS, VM swap, Preboot), local snapshots, and folders BitReaper isn't
allowed to read. Granting Full Disk Access shrinks it.

**I deleted files but free space didn't go up.** Items in the Trash still take space until you empty it, and Time
Machine's local snapshots can keep deleted data around as purgeable space for a while.

## License

[MIT](LICENSE) © The BitReaper contributors.
