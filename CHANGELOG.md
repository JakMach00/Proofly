# Changelog

## 3.0.1

- Crop works with a frame now. Choosing Crop puts a frame on the screenshot with a grip on
  every corner and side. Drag the grips to adjust it, drag inside to move it, and the part
  that would be cut away is darkened as you go. Nothing is cropped until you press Apply
  crop or Enter. Cancel or Esc leaves the screenshot as it was. Opening Crop again on a
  cropped screenshot shows the whole image, so a crop can also be widened.
- Click marks in recordings follow the mouse button. The ring appears when a button goes
  down, stays while it is held, and fades out after it is released, instead of flashing for
  a fixed time.
- The colours moved from the tool row to the options row, next to Thickness. The options
  row wraps in a narrow window as well.
- "Include the mouse pointer" is on by default.
- A zoomed screenshot can be dragged around with the Select tool: hold the left mouse button
  on an empty spot and move.
- "Start a new session after saving" is off by default. Saving leaves you in the same session.
- The session list shows names only, without the item count that looked like a second
  number. A lone session that was never renamed is shown as "Session", numbers appear from 1
  once there is more than one.
- The editor toolbar no longer hides Undo in a narrow window. All ten tools fit on one line
  at the smallest window size, and Undo and Delete selected always stay visible. To make
  room, "Unsaved changes" moved up next to Save changes, the hint about the arrow keys became
  a tooltip on Previous and Next, and "Reset crop" is now the shorter "Undo crop".
- The options row stays on one line with the Text tool: Thickness, which text does not use,
  makes way for font, size and outline.

## 3.0.0

Proofly is rewritten in C# with WPF on .NET 8. The Electron and Node toolchain is gone, the
application builds with the .NET SDK and needs one NuGet package.

New

- Sessions. Evidence is kept in named sessions that live on disk from the moment something
  is captured. A session survives a crash or a restart, and a saved session can be reopened
  later to add to it and save the document again. New, Rename and Delete session sit above
  the gallery.
- After saving a document a fresh session becomes current and the saved one stays in the
  list. This replaces "Clear after saving the PDF".
- Screenshots can be dragged into a different order. Every card shows the page it will be on
  in the document. While dragging, the card fades and a small copy follows the pointer, the
  other cards slide to their new places and the moved card is marked for a moment.
- Word export. The document can be saved as PDF or as .docx, one page per screenshot in both.
- Paste from the clipboard with Ctrl+V or the sidebar button, for copied images and for image
  files copied in Explorer.
- Zoom in the editor: Ctrl with the mouse wheel, the buttons in the options row, Ctrl+0 to
  fit and Ctrl+1 for 100%. The wheel scrolls a zoomed image, the middle button drags it.
- Recordings can be paused and resumed, from the sidebar or with Ctrl+Shift+F10. The paused
  part is simply not in the file.
- Mouse clicks can be highlighted in recordings, with a ring that stays visible for a good
  half second.
- The mouse pointer can be included in screenshots.
- Dark text gets a white outline instead of a dark one.

Changed

- The exported document is named after the session.
- Screenshots are stored as files instead of being held in memory, which keeps long sessions
  light.
- The PDF and the Word file are written by small modules inside the application, so no
  document library is needed.
- Recording uses ScreenRecorderLib, which encodes with Media Foundation. Recordings are MP4
  (H.264) only, the WebM option is removed because Windows has no built in VP9 encoder.
- Typing into a new text label and placing it are one undo step, so undo no longer leaves an
  empty label behind.
- Saving the editor updates the size shown on the gallery card after a crop.

Removed

- Trimming a recording and saving a faster copy of it. Pausing while recording covers most of
  what trimming was used for, and the Windows Photos app trims a finished file.

## 2.1.0

- New Marker tool in the editor: a freehand, semi-transparent stroke that follows the mouse, for
  underlining or circling something by hand where the Highlight box is too rigid. It starts
  yellow like the highlighter, takes the thickness setting, can be moved with Select and undone
  like any other annotation.
- Upgraded jsPDF from 2.5.2 to 4.2.1, which also brings DOMPurify 3. This clears the critical and
  the moderate advisory that `npm audit` reported for the libraries shipped with the app, so a
  dependency scan of the runtime packages now comes back clean. PDF export was checked end to
  end after the upgrade: one page per screenshot, orientation per image, no text on the pages.

## 2.0.1

- Removed the update check. Proofly no longer contacts GitHub, or anything else, at startup or on
  demand, so it makes no network requests at all. The check could not work on networks that
  block public hosts, and the "Check for updates" button, the "Check on startup" option and the
  new version banner went with it.
- The README explains the two downloads that reach beyond npm during installation and how to
  point them at an internal mirror.

## 2.0.0

- The application is now called Proofly. The window, the tray, notifications, the built package
  and the settings all use the new name.
- Full screen capture can be locked to a region. Select the area once and every following
  capture takes just that area, which removes reselecting the same part of the screen for every
  step of a test. Reset brings the whole screen back. Locked captures go to the gallery only
  and stay out of the clipboard, so a long series does not flood it.
- Fixed the gallery squeezing its rows instead of scrolling once there were enough items, which
  clipped the Delete button off the bottom of every card and forced zooming out to reach it.
- The Left and Right arrow keys switch between screenshots in the editor, through the same
  unsaved changes prompt as the Previous and Next buttons.
- The editor can copy the current screenshot to the clipboard (Copy or Ctrl+C) or save it as a
  PNG or JPG file, annotations and crop included, without saving it into the session. That
  covers annotating one region to paste somewhere and carrying on with the rest.
- The editor remembers the last tool, colour, thickness and text settings between screenshots
  instead of returning to Arrow each time.
- The options row under the tools keeps one height whichever tool is active, so the image no
  longer jumps when switching between Select and a tool with more settings.

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

- The application identifier is Proofly, so notifications name the sender Proofly instead
  of pl.jakub.proofly.

## 1.8.1

- Highlighting no longer offers a thickness, for the same reason redaction does not: both are
  solid fills.
- The step number field only appears while the step tool or a step is selected.
- Placing a text box hands the keyboard to it straight away, so typing starts without clicking
  into the content field first. The box grows with the text and can no longer be dragged to a
  size by hand.
- Starting a recording with "Hide Proofly while capturing" enabled minimizes the window, so it
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
- Starting a recording with "Hide Proofly while capturing" enabled minimizes the window, so it
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
