PETAL OS DESKTOP / Source build

Our own Windows x64 wallpaper host. No Lively dependency.

Build on Windows with .NET Framework 4.8 and PowerShell:
  .\build.ps1

The script uses the Windows Framework C# compiler, creates build/PetalOS,
and bundles pinned Microsoft.Web.WebView2 1.0.4078.44 SDK binaries.
It first checks the standard NuGet cache, then downloads that public package
to dependencies/ if missing. Only building may need internet.

Keep web/ beside Program.cs in a source checkout. When building from the
GitHub repository, the root web design is used by passing:
  .\desktop\build.ps1 -WebSource $PWD -OutputPath .\build\PetalOS

Run build/PetalOS/PetalOS.exe. The app needs Edge WebView2 Runtime and
.NET Framework 4.8; it does not install or modify either prerequisite.

Native verification (automatically restores desktop and exits):
  PetalOS.exe --smoke-test --data-dir C:\path\to\isolated-test-folder
Read native-smoke.json in that folder. This test briefly creates a real
wallpaper window behind icons. It does not change the static Windows
wallpaper or add startup entries.

Architecture:
- Host: settings persistence, renderer synchronization, tray and lifecycle.
- Editor: ordinary WinForms window with WebView2 and a native toolbar.
- Wallpaper: borderless child window attached to an Explorer WorkerW layer.
- Desktop: Windows API interop and compatible background-layer discovery.
- JavaScript: sends settings only from our editor; receives native state
  without generating a feedback loop.

Desktop input remains owned by Windows. This prototype edits through our
own window, rather than using global mouse/keyboard hooks.

WorkerW is an undocumented Explorer convention. It may differ across Windows
builds or desktop utilities. Failure leaves the normal desktop in place.
Multiple display selection and automatic reattachment logic are included,
but only a single-display Windows 10 setup has been verified so far.

Our code and art: MIT. See web/LICENSE.txt.
WebView2 SDK: Microsoft terms, shipped in licenses/ with the app.
