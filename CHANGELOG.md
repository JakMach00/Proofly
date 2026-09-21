# Changelog

## 1.9.0

### Capture

- Print Screen captures a region. Selection happens on the screen itself, in a frozen full
  screen overlay like Snipping Tool, instead of inside the application window.
- A captured region goes straight into the Windows clipboard as well as into the gallery.
- A shortcut captures the display under the pointer, the sidebar button the display chosen in
  the sidebar. Recording a region uses the same overlay.
- Existing custom shortcuts are kept, only the region binding moves to Print Screen.

### Background running

- Start with Windows, off by default. The app starts hidden in the tray when you sign in.
- The window X hides the app from the taskbar while it keeps running in the tray, and the first
  close of a session says so in a tray notice. Minimize still sends it to the taskbar.
- The tray menu offers opening the window, capturing a region and quitting.
- Only one copy runs at a time. Launching it again brings the running window forward.

### Editor

- Text is edited in place, directly on the image inside its dashed frame. The separate content
  field is gone.
- Placing a new text box starts typing straight away, double clicking an existing label opens
  it for editing, and Escape or a click elsewhere finishes.
- A text box left empty disappears without leaving an undo step behind.
- Selecting or grabbing something without moving it no longer counts as an unsaved change,
  and neither does a click that draws nothing.

### Other

- The application identifier is ScreenApp, so notifications name the sender ScreenApp instead
  of pl.jakub.screenapp.

## 1.8.1

- Highlighting no longer offers a thickness, for the same reason redaction does not: both are
  solid fills.
- The step number field only appears while the step tool or a step is selected.
- Placing a text box hands the keyboard to it straight away, so typing starts without clicking
  into the content field first. The box grows with the text and can no longer be dragged to a
  size by hand.
- Starting a recording with "Hide ScreenApp while capturing" enabled minimizes the window, so it
  stays out of the recording after the region picker brought it back into view.

## 1.8.0

### Editor

- Undo is a real history now. It steps back through deletions, moves, property changes and
  crops, instead of only removing the most recently drawn annotation.
- Text is far more flexible: Enter starts a new line, the size is its own control rather than
  being tied to thickness, the outline is an optional checkbox, and there are five fonts.
- Screenshots can be cropped. Annotations are held in the coordinates of the original image, so
  a crop never moves them, and "Reset crop" brings the full frame back.
- Recordings can be trimmed. Set the start and the end from the playback position, and apply it
  together with a speed change in one pass.
- Redaction no longer offers a thickness, because it is a solid block that covers something.
- Leaving the highlighter returns the colour to red instead of keeping yellow.

### Elsewhere

- Deleting a screenshot or a recording can be undone with Ctrl+Z or the action in the status
  bar, including Delete all.
- Fixed the nonsense length, such as 21452:24:56, that files carried after recording or editing.
  MediaRecorder writes no overall duration, so WebM now gets the missing metadata section and
  MP4 gets the duration written into its header boxes. Without this, seeking and trimming were
  unusable.

## 1.7.2

- No sidebar action is styled as preselected any more. The accent is left to the export button
  and to the first action in the empty workspace.
- The export button reads "Save PDF", or "Save recordings" for a recordings only session, and
  stays disabled until something has been captured. The longer label did not fit next to the
  shortcut badge. Recordings are still written next to the PDF.

## 1.7.1

- The Capture full screen button in the sidebar is no longer filled with the accent colour. A
  filled button among a stack of plain ones reads as a selected state rather than as emphasis.
  The accent now marks only the export action and the call to action in the empty workspace.

## 1.8.1

- Highlighting no longer offers a thickness, for the same reason redaction does not: both are
  solid fills.
- The step number field only appears while the step tool or a step is selected.
- Placing a text box hands the keyboard to it straight away, so typing starts without clicking
  into the content field first. The box grows with the text and can no longer be dragged to a
  size by hand.
- Starting a recording with "Hide ScreenApp while capturing" enabled minimizes the window, so it
  stays out of the recording after the region picker brought it back into view.

## 1.8.0

### Editor

- Undo is a real history now. It steps back through deletions, moves, property changes and
  crops, instead of only removing the most recently drawn annotation.
- Text is far more flexible: Enter starts a new line, the size is its own control rather than
  being tied to thickness, the outline is an optional checkbox, and there are five fonts.
- Screenshots can be cropped. Annotations are held in the coordinates of the original image, so
  a crop never moves them, and "Reset crop" brings the full frame back.
- Recordings can be trimmed. Set the start and the end from the playback position, and apply it
  together with a speed change in one pass.
- Redaction no longer offers a thickness, because it is a solid block that covers something.
- Leaving the highlighter returns the colour to red instead of keeping yellow.

### Elsewhere

- Deleting a screenshot or a recording can be undone with Ctrl+Z or the action in the status
  bar, including Delete all.
- Fixed the nonsense length, such as 21452:24:56, that files carried after recording or editing.
  MediaRecorder writes no overall duration, so WebM now gets the missing metadata section and
  MP4 gets the duration written into its header boxes. Without this, seeking and trimming were
  unusable.

## 1.7.2

- No sidebar action is styled as preselected any more. The accent is left to the export button
  and to the first action in the empty workspace.
- The export button reads "Save PDF", or "Save recordings" for a recordings only session, and
  stays disabled until something has been captured. The longer label did not fit next to the
  shortcut badge. Recordings are still written next to the PDF.

## 1.7.1

- Every option carrying an explanation now shows a small info icon, so it is obvious that
  hovering it says what the option does. Focusing the icon shows the same text without a mouse.
- Restored the accent on the main capture action, which had been lost with the mode switch.

## 1.7.0

Interface only, no change to capture, recording, export, shortcuts or IPC.

- Split the window into a title bar, sidebar, workspace and status bar, with the sidebar and the
  workspace scrolling independently.
- Grouped the sidebar into Source, Capture, Recording, Output, Export and Utilities, numbered by
  a CSS counter so the mode switch cannot put the numbers out of order.
- Replaced the mode switch and the brand mark in the title bar with an animated light and dark
  toggle. The empty workspace offers both capture and record as first actions.
- Reworked the empty workspace: what to do next, the primary action with its shortcut, and the
  display, audio and quality currently in use.
- Moved to a single lime accent used only for the primary action, active states and positive
  status, with consistent button heights and visible hover, focus, disabled and checked states.
- Replaced the native confirm on Delete all with an in-app dialog that says what will be removed.
- Editor: clicking an existing step or text label with the same tool active moves it instead of
  creating another one on top.
- Editor: undoing or deleting the most recent step returns its number to the sequence.
- Editor: the highlighter starts yellow.

## 1.6.0

- Checks GitHub at startup for a newer release and offers a link to the release page. Nothing is
  downloaded or installed.
- The check runs through the main process so it follows system proxy settings, times out after
  eight seconds, and stays silent on failure unless it was started by hand.
- A dismissed version is not announced again, and the check can be switched off entirely.
- Manual "Check for updates" button in the sidebar.
- PDF pages now hold the screenshot and nothing else, with no file name and no timestamp header.
- Recordings no longer produce PDF pages, they are exported as files only.
- The screen list is rebuilt when a display changes resolution or is added or removed, instead
  of staying stale until the next restart.
- No sidebar button looks preselected on launch.
- Tooltips appear after one second and next to the control rather than in the corner of the
  screen.

## 1.5.0

- Recordings can be exported without a PDF. With no screenshots the export button switches to
  recordings only, and when both are present a separate button writes just the clips.
- Recordings exported on their own keep their own file names and go to a folder of your choice.
- Every sidebar option explains itself in a tooltip after a two second hover.
- The running version is shown in the bottom right corner, read from package.json at build time.

## 1.4.0

- Recordings can be written as MP4 (H.264), which Windows Media Player Legacy can open, or as
  WebM (VP9) for smaller files. MP4 is the default.
- MP4 support is checked at runtime and falls back to WebM with a note when the H.264 encoder is
  missing, instead of failing the recording.
- Exported file names and the speed conversion follow the container the clip was recorded in.

## 1.3.0

- Audio recording from the Windows default devices: microphone, system audio through loopback,
  or both mixed. Off by default.
- Audio problems degrade to a silent recording with a reason on the status bar instead of
  aborting the capture.
- The speed conversion keeps the audio track and holds its pitch.
- The Mute toggle is disabled and labelled for clips that carry no audio track.

## 1.2.0

- Recordings can be sped up to 1.1x, 1.25x, 1.5x, 1.75x or 2x. The speed row changes the preview
  instantly, and applying it re-encodes the file so exports really are shorter.
- Conversion runs at playback speed with a progress figure and only replaces the clip once the
  new file exists.
- Mute toggle in the player. Recordings contain no audio track, so it affects playback only.

## 1.1.0

- Fixed "Open output folder", which used `shell.showItemInFolder` and failed silently on
  Windows. It now opens the folder through `shell.openPath` and reports any error.
- Added an optional fixed export folder. It is off by default, so the save dialog keeps
  appearing until the option is switched on and a folder is picked.
- Exports no longer overwrite: a name already in use gets a counter.
- If the chosen folder is unavailable, the export falls back to the dialog and says so.

## 1.0.0

First public release.

### Capture

- Screenshots of a selected screen at native resolution through `desktopCapturer`,
  so the image is not resampled by the WebRTC pipeline.
- Region capture performed on a frozen frame, which keeps the selection precise
  because the image does not move while it is being selected.
- Screen list built by the application rather than taken from `source.name`, which
  the operating system returns localized.
- Screenshots taken with a shortcut never pull the window to the front: a minimized
  window stays minimized and a background window comes back unfocused.
- The window is not hidden at all when it sits on a different monitor than the one
  being captured.

### Recording

- Screen recording through `MediaRecorder` with VP9 and an explicit bitrate, in three
  size profiles. Region recording goes through a canvas pipeline.
- Fallback from `getUserMedia` desktop constraints to `getDisplayMedia`, served by
  `setDisplayMediaRequestHandler`, so a change in Electron cannot break capture outright.
- `backgroundThrottling` disabled so the recording loop keeps its frame rate while the
  window is minimized.

### Annotation

- Canvas editor with arrows, boxes, ellipses, numbered steps, text, highlight and redaction.
- Annotations stay editable after they are drawn: handles reshape a selection, and colour,
  thickness, step number and text apply to the selected item live.
- Leaving a screenshot with unsaved annotations asks whether to save, discard or stay.

### Export

- PDF export with one page per screenshot, orientation derived from the image, and a page
  per recording carrying its first frame and length.
- Recordings written as separate files next to the PDF, because embedding playable video in
  a PDF only works in Adobe Acrobat.
- Optional clearing of the session after a successful export, remembered between runs.
- Optional fixed export folder, off by default, which skips the save dialog while it is set.
- Exports never overwrite: a name already in use gets a counter.

### Interface and shortcuts

- Global shortcuts that work while the window is minimized, rebindable in the application.
- Defaults avoid plain Ctrl combinations, which a global shortcut would take away from every
  other application, and Ctrl+Alt combinations, which AltGr reproduces on international
  keyboard layouts.
- Dark and light theme following the system preference on first run.

### Build

- Packaged with electron-builder into a zip holding the complete ready to run app folder.
- GitHub Actions workflow building and publishing on `v*` tags.
