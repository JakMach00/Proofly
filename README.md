# Proofly

Proofly is a Windows desktop tool for collecting test evidence. It takes screenshots, lets
you mark them up and describe them, records the screen, and saves the result as a PDF or
Word document with one screenshot per page.

It is built for manual testing: capture each step with a shortcut while you work in the
application under test, annotate what matters, add a note to each step, and export one
document at the end.

## Contents

- [What it does](#what-it-does)
- [Requirements](#requirements)
- [Running it](#running-it)
- [A typical session](#a-typical-session)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Sessions](#sessions)
  - [Notes](#notes)
  - [Selecting several items](#selecting-several-items)
- [The exported document](#the-exported-document)
- [Recording](#recording)
- [Data and privacy](#data-and-privacy)
- [Removing Proofly](#removing-proofly)
- [Building from source](#building-from-source)
- [Third party components](#third-party-components)
- [Limitations](#limitations)
- [Project layout](#project-layout)

## What it does

Capture

- Full screen capture of the selected display, at its native resolution.
- Region capture: the screen freezes and you drag a rectangle over it. The result is added
  to the session and copied to the clipboard. The shortcut freezes the display the mouse
  pointer is on, the sidebar button freezes the display selected under Source.
- Locked region: select an area once, and every following full screen capture takes just
  that area until you reset it. These captures are not copied to the clipboard.
- Paste: an image on the clipboard, or image files copied in Explorer (PNG, JPG, BMP), can
  be added to the session.
- The mouse pointer is drawn into screenshots. This can be switched off.
- Optionally the Proofly window hides itself for the moment of the capture.

Annotate

- Tools: Arrow, Box, Ellipse, numbered Step, Text, Highlight, Marker, Redact and Crop.
- Text is typed directly on the image. Double click an existing label to edit it.
- Crop shows a frame with grips on its corners and sides. Adjust it until it is right, then
  apply it. Nothing is cut until then.
- Six colours, adjustable thickness, five fonts, optional text outline.
- Every change can be undone, up to 50 steps back, including deletions, moves and crops.
- A note per screenshot, typed under the image, for the document.
- Zoom up to 600% for precise work on large screenshots.
- Copy the current image to the clipboard, or save it as a PNG or JPG file, at any time.

Record

- The whole screen or a selected region, saved as MP4 (H.264).
- Pause and resume.
- Audio: none, microphone, system audio, or both.
- Optional highlighting of mouse clicks.
- An on-screen sign while recording, with the elapsed time, that does not appear in the
  video.
- Recordings play inside Proofly, with a speed preview from 1x to 2x and a mute switch.
  This affects playback only, the file is not changed.

Organise

- Evidence is kept in named sessions that are written to disk as you capture, so they are
  still there after a restart or a crash.
- Screenshots can be dragged into a different order. Each one shows the page number it will
  have in the document.
- Each screenshot can have a note that is printed under it in the document.
- Several items can be selected and then deleted or saved together.
- A deletion can be undone with Ctrl+Z until the next deletion.

Export

- PDF or Word (.docx), the whole session or only the selected items.
- Optionally with the step number, the note and the capture time under each screenshot.
- Recordings are saved as separate files next to the document.

Other

- Dark and light theme.
- Optional start with Windows, hidden in the notification area.

## Requirements

- Windows 10 or Windows 11, 64 bit.
- .NET Desktop Runtime 8 or newer. A build published as self-contained carries its own
  runtime and does not need this.
- The Microsoft Visual C++ runtime (x64), needed for recording only. The release packages
  include its files, so nothing has to be installed. A build you publish yourself does not
  include them and relies on the Visual C++ Redistributable 2015-2022 being installed.
- Media Foundation, which is part of Windows. The N editions of Windows need the Media
  Feature Pack for recording.
- Windows 10 version 2004 or newer for the on-screen recording sign. On older versions
  recording works, but the sign is not shown.

## Running it

A published build is a folder. Copy it anywhere and start `Proofly.exe`. There is no
installer. Proofly keeps its own data in your user profile only, see
[Data and privacy](#data-and-privacy).

Proofly keeps running in the notification area (system tray) when its window is closed, so
the shortcuts stay available. To exit, right click the tray icon and choose Quit.

Starting Proofly a second time brings the running window to the front.

## A typical session

1. Start Proofly. Pick the display to capture under Source if you have more than one.
2. Switch to the application under test.
3. Press the capture shortcut at each step. Use Print Screen when you only need part of the
   screen.
4. Back in Proofly, click a screenshot to open the editor. Add arrows, step numbers or
   text, cover anything that must not be shown with Redact, then Save changes. Write what
   the step shows in the Note field under the image.
5. Drag the screenshots into the right order if needed.
6. Choose PDF or Word under Export and save.

After saving, the session stays open, so you can add more screenshots and save the document
again. Use New to start a separate session for the next piece of work. The earlier one stays
in the session list and can be reopened at any time.

## Keyboard shortcuts

Global shortcuts work while Proofly is minimized or hidden in the tray. All of them can be
changed under Utilities, Keyboard shortcuts.

| Action | Default |
| --- | --- |
| Capture the whole screen (or the locked region) | Ctrl+Shift+F9 |
| Capture a region | Print Screen |
| Start and stop recording | Ctrl+Shift+F11 |
| Pause and resume recording | Ctrl+Shift+F10 |
| Save the document and recordings | Ctrl+Shift+F12 |

Print Screen is delivered to Proofly only when the Windows setting "Use the Print screen key
to open screen capture" (Settings, Accessibility, Keyboard) is switched off.

Avoid shortcuts that combine Ctrl and Alt. On Polish and other international keyboard
layouts the right Alt key sends Ctrl+Alt, so such a shortcut fires while typing accented
letters.

In the main window

| Action | Key |
| --- | --- |
| Undo the last deletion | Ctrl+Z |
| Paste from the clipboard | Ctrl+V |
| Select or unselect an item | Ctrl+click |
| Select a range of items | Shift+click |
| Select everything | Ctrl+A |
| Delete the selected items | Delete |
| End selecting | Esc |

In the editor

| Action | Key |
| --- | --- |
| Save changes | Ctrl+S |
| Undo | Ctrl+Z |
| Copy the image as it looks now | Ctrl+C |
| Delete the selected annotation | Delete |
| Previous and next screenshot | Left, Right |
| Zoom in and out | Ctrl+Plus, Ctrl+Minus, or Ctrl with the mouse wheel |
| Zoom to 100% | Ctrl+1 |
| Fit the whole screenshot | Ctrl+0 |
| Apply the crop frame, or close it without cropping | Enter, Esc |
| Close the editor (asks first when there are unsaved changes) | Esc |
| Leave the Note field without closing the editor | Esc, while typing a note |

When zoomed in, the mouse wheel scrolls the image and Shift with the wheel scrolls sideways.
To drag the image around, hold the left mouse button on an empty spot with the Select tool,
or use the middle mouse button with any tool.

## Sessions

A session is one set of evidence: its screenshots and recordings in a fixed order. The
controls above the gallery manage them.

- New starts an empty session. The current one is kept.
- Rename changes the name. The name is also the default file name of the exported document.
- Delete session removes the session and all its files from the computer. This cannot be
  undone.
- Delete all removes every screenshot and recording from the current session but keeps the
  session itself. Ctrl+Z brings them back until the next deletion or until another session
  is opened.

With "Start a new session after saving" switched on, saving a document keeps the session and
opens a new one. It is off by default, so saving leaves you in the same session.

A session that was never renamed is called "Session" while it is the only one. With more
than one they are numbered from 1: "Session 1", "Session 2" and so on.

Empty sessions that still have their default name are removed the next time Proofly starts,
except the one that was open last.

### Notes

The editor has a Note field under the image. What you type there belongs to the screenshot
and is stored with the session as soon as you leave the field. It does not need Save
changes and is not affected by discarding image changes. Esc in the field leaves it without
closing the editor. A note can be up to 500 characters long. In the gallery, a screenshot
with a note shows a Note mark, and hovering over the mark shows the text.

Recordings have no notes.

### Selecting several items

Ctrl+click a card to select it, Shift+click to select everything between it and the card
clicked before, Ctrl+A to select all. Each card also shows a tick box when the pointer is
over it. While anything is selected, a plain click selects or unselects instead of opening,
and a bar above the gallery offers:

- Save selected: saves only the selected items, with the current export settings. The
  steps in that document are numbered from 1 and its default name ends in "- selected".
  The session is not marked as saved and no new session is started.
- Delete selected (or the Delete key): removes them in one step. Ctrl+Z brings all of them
  back.
- Select all and Clear (or Esc).

## The exported document

- A4 pages, one screenshot per page, in the order shown in the gallery.
- Each page is portrait or landscape to suit its screenshot.
- The screenshot is scaled to fit inside a 12.7 mm (36 pt) margin and centred. There are no
  headers, footers or file names.
- With "Print notes under screenshots" (on by default), a screenshot that has a note gets
  it printed underneath, headed "Step n". With "Print the capture date and time" (off by
  default), the heading also says when the screenshot was taken or pasted, for example
  `Captured 2026-10-08 12:01:33 (UTC+02:00)`. The screenshot is made smaller by as much as
  the text needs, so both always fit on one page. A page with nothing to print shows only
  the screenshot.
- Notes longer than 12 printed lines are cut and end in "...". In the PDF, text is set in
  Helvetica, which covers Western European and Polish letters. Other scripts, such as
  Cyrillic, print as question marks in the PDF. The Word document uses Arial and shows any
  character.
- "Compress images in the document" stores screenshots as JPEG at quality 85, which makes
  the file much smaller at the cost of slightly softer text. Switched off, screenshots are
  stored without loss.
- The document is named after the session, for example `Login tests.pdf`. A document saved
  from a selection is named `Login tests - selected.pdf`.
- Recordings are copied next to the document as `<document name>_recording_01.mp4`,
  `_02` and so on.

"Always use one folder" saves straight into a folder you choose, without a dialog. In that
folder existing files are never overwritten: a repeated name gets a counter such as
`Login tests (2).pdf`. When the option is off, the normal save dialog is shown and asks
before replacing a file.

## Recording

| Quality | Frames per second | Bitrate | Scale |
| --- | --- | --- | --- |
| Low | 10 | 0.5 Mbit/s | 60% |
| Medium | 15 | 1.2 Mbit/s | 80% |
| High | 20 | 3 Mbit/s | 100% |

- Low is enough to document a defect. High keeps small text sharp.
- The mouse pointer is always visible in recordings.
- Frames are written at the full rate of the quality setting even while nothing on the
  screen changes. Such recordings play reliably in other players too, and the size of the
  file depends on its length, not on how much happened on the screen.
- With "Highlight clicks in recordings" a ring is shown around the pointer while a mouse
  button is held, yellow for the left button and red for the right, and fades out after the
  button is released. The ring is drawn on the screen during the recording, so you see it
  too.
- While a recording is paused nothing is written, so the paused part does not appear in the
  file.
- While a recording runs, a see-through badge in the top right corner of the recorded area
  shows REC and the elapsed time, or PAUSED. Starting, pausing and resuming also show a
  large symbol in the middle of the area for a moment. Both are visible only on your
  screen: Windows leaves them out of the recording and of screenshots. This needs Windows 10
  version 2004 or newer. On older versions they are not shown. Clicks pass through them.
- With "Hide Proofly while capturing" switched on, the window minimizes when a recording
  starts.
- Microphone means the default Windows input device, system audio means what the default
  output device is playing. If a device is missing, the recording runs without that source
  and the status bar says so.
- A recording that is still running when Proofly stops unexpectedly cannot be recovered. A
  finished recording is safe.

## Data and privacy

Proofly makes no network connections. It does not send, upload or synchronise anything.

Where things are stored

| What | Location |
| --- | --- |
| Sessions: screenshots, recordings, previews | `%LOCALAPPDATA%\Proofly\Sessions`, one folder per session |
| Settings | `%APPDATA%\Proofly\settings.json` |
| Error log | `%APPDATA%\Proofly\error.log`, created only when an error occurs |
| Exported documents | wherever you save them |

Points to be aware of

- Screenshots and recordings stay on the computer until their session is deleted in
  Proofly. Saving a document does not remove them. Delete sessions you no longer need,
  especially when they show confidential data.
- Redact covers an area with a solid black block. Once the changes are saved, the covered
  pixels are replaced in the stored screenshot and cannot be brought back. Until you save,
  the block can still be moved or removed.
- Saving changes in the editor merges the annotations into the screenshot. After that they
  can no longer be edited separately.
- Notes are stored as plain text in `session.json` in the session folder and are deleted
  with the session. With "Print notes under screenshots" on, they also end up in the
  document, so write them with the same care as anything else in the evidence.
- The printed capture time includes the offset from UTC of the computer that took the
  screenshot.
- The on-screen recording sign is excluded from capture by Windows, so it never appears in
  recordings or screenshots.
- A region capture is copied to the Windows clipboard, and so is anything you copy from the
  editor. Other applications can read the clipboard.
- "Start with Windows" adds one entry for the current user under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Switching the option off removes it.
  No other registry entries are created.

## Removing Proofly

1. Switch off "Start with Windows" if it is on.
2. Quit Proofly from the tray icon.
3. Delete the application folder.
4. Delete `%LOCALAPPDATA%\Proofly` and `%APPDATA%\Proofly` to remove all sessions and
   settings.

## Building from source

You need the .NET SDK 8 and access to a NuGet feed that provides the ScreenRecorderLib
package.

```
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

To produce a folder that can be handed to others:

```
dotnet publish -c Release -o publish
```

This build needs the .NET Desktop Runtime on the target computer. To include the runtime,
at the cost of a much larger folder:

```
dotnet publish -c Release -o publish --self-contained true
```

The project targets x64 only, because the recording library is native.

Do not publish as a single file (`PublishSingleFile`). ScreenRecorderLib is a mixed C++/CLI
assembly, which .NET cannot load from inside a single-file bundle, so recording fails with a
BadImageFormatException. Publish as a folder, as above.

## Third party components

| Component | Use | Licence |
| --- | --- | --- |
| [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib) 7.0.1 | Screen recording | MIT |
| .NET and WPF | Runtime and user interface | MIT |
| Microsoft Visual C++ runtime files (release packages only) | Needed by ScreenRecorderLib | Microsoft redistributable terms |

Recordings are encoded with the H.264 encoder that is part of Windows. Proofly ships no
encoder of its own. The PDF and Word files are written by code in this repository, without
a document library.

## Limitations

- Windows only, 64 bit only.
- Recordings are MP4 (H.264) only. There is no trimming or editing of a finished recording.
- The executable is not code signed, so Windows may show a warning the first time it is
  started.
- Only one recording can run at a time, and the session cannot be changed while it runs.
- Recordings have no notes, and the document has no title page.
- In the PDF, notes can use Western European and Polish letters only. Other scripts print
  as question marks. The Word document has no such limit.
- Dragging moves one card at a time, also while several are selected.
- Publishing as a single file is not supported, see [Building from source](#building-from-source).

## Project layout

```
Core/        PDF and Word writers, caption layout, session store, settings, shortcut parsing,
             file naming (no user interface dependencies)
Models/      Session items and annotations
Services/    Screen capture, recording, recording indicator, shortcuts, tray icon, start with
             Windows, themes
Controls/    Gallery panel and the drawing surface of the editor
Views/       Editor, player, shortcut settings, region overlay, dialogs
Themes/      Colours and control styles
```
