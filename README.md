# Proofly

A small Windows tool for documenting test evidence: screenshots, annotations, screen
recordings and a PDF export. Everything stays on the machine until it is exported, and the
application makes no network requests.

Version 3 is written in C# with WPF. Versions up to 2.1.0 were an Electron application.

## Requirements

To run:

- Windows 10 or Windows 11, 64 bit
- .NET Desktop Runtime 8 or newer (the entry `Microsoft.WindowsDesktop.App` in
  `dotnet --info`), or the self-contained package, which needs no runtime at all
- Visual C++ Redistributable 2015-2022 (x64), needed for recording only

To build:

- .NET SDK 8
- Access to a NuGet feed that carries `ScreenRecorderLib`

## Build and run

```
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

A folder that can be copied to another machine:

```
dotnet publish -c Release -o publish
```

The result needs the .NET Desktop Runtime 8 or newer on the target machine. To ship the
runtime with the application instead, at the cost of a much larger folder:

```
dotnet publish -c Release -o publish --self-contained true
```

### Building behind a corporate package proxy

Point NuGet at the internal feed in the user level configuration, for example with
`dotnet nuget add source <address> -n internal` and `dotnet nuget disable source nuget.org`.
That file lives in the user profile. Do not add a `NuGet.Config` with an internal address to
this repository, the `.gitignore` already keeps one out.

## Features

Capture

- Full screen capture of the selected display at native resolution
- Region capture on a frozen screen, copied to the clipboard and added to the gallery
- Lock a region once and capture the same area repeatedly
- Paste an image from the clipboard into the session
- Optional mouse pointer in screenshots
- The window can hide itself while capturing

Sessions

- Everything captured is written to disk at once and is still there after a crash or a restart
- Named sessions, so several pieces of work can be kept apart and reopened later
- Drag screenshots into the order they should have in the document

Recording

- Whole screen or a region, to MP4 (H.264)
- Pause and resume
- No audio, microphone, system audio, or both
- Optional highlighting of mouse clicks
- Three quality levels that set frame rate, bitrate and scale together

Editor

- Arrow, Box, Ellipse, numbered Step, Text, Highlight, Marker, Redact and Crop
- Text is typed directly on the image, double click edits an existing label
- Undo for every change, including deletions, moves and crops
- Zoom and pan for precise work on large screenshots
- Copy to the clipboard and Save as PNG or JPG without leaving the session

Export

- PDF or Word (.docx) with one page per screenshot and nothing else on the page
- Recordings saved as separate files next to the document
- Optional fixed output folder, files are never overwritten there

Other

- Global shortcuts that can be rebound in the app
- Tray icon, closing the window keeps the app running there
- Optional start with Windows
- Dark and light theme

## Default shortcuts

| Action | Shortcut |
| --- | --- |
| Capture whole screen | Ctrl+Shift+F9 |
| Capture region | Print Screen |
| Start and stop recording | Ctrl+Shift+F11 |
| Pause and resume recording | Ctrl+Shift+F10 |
| Save the document and recordings | Ctrl+Shift+F12 |

Print Screen belongs to Windows while Settings, Accessibility, Keyboard, "Use the Print
screen key to open screen capture" is switched on.

## Project layout

```
Core/        PDF and Word writers, session store, settings, shortcut parsing, file naming
             (no UI dependencies)
Models/      Session items and annotations
Services/    Screen capture, recording, shortcuts, tray, autostart, themes
Controls/    Gallery panel and the drawing surface of the editor
Views/       Editor, player, shortcut settings, region overlay, confirmation dialog
Themes/      Colours and control styles
```

## Where things are stored

- Settings: `%APPDATA%\Proofly\settings.json`
- Sessions: `%LOCALAPPDATA%\Proofly\Sessions`, one folder per session

A session stays on disk until it is deleted in the app with "Delete session". Screenshots and
recordings of confidential systems are therefore kept on the machine for as long as their
session exists, so delete sessions that are no longer needed.

## Third party

- [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib), MIT licence

Recordings use H.264 through the encoder that ships with Windows.
