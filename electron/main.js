'use strict';

const {
  app,
  BrowserWindow,
  Menu,
  Tray,
  clipboard,
  ipcMain,
  desktopCapturer,
  globalShortcut,
  nativeImage,
  net,
  screen,
  session,
  dialog,
  shell,
} = require('electron');
const path = require('node:path');
const fs = require('node:fs/promises');

const isDev = !app.isPackaged;
// The repository on GitHub is still named ScreenApp. Change this only if
// the repository itself is renamed.
const REPO = 'JakMach00/ScreenApp';
const DEV_URL = 'http://localhost:5173';

/** @type {BrowserWindow | null} */
let win = null;

/** @type {Tray | null} */
let tray = null;

/** @type {BrowserWindow | null} */
let overlay = null;

/** Set on a real quit, so closing the window can hide it to the tray instead. */
let isQuitting = false;

/** The "still running" notice is shown once per session, not on every close. */
let trayNoticeShown = false;

/** Launched by Windows at sign in, which should stay out of sight. */
const startHidden = process.argv.includes('--hidden');
const ICON_PATH = path.join(__dirname, '..', 'build', 'icon.ico');

/** Source the renderer wants when it falls back to getDisplayMedia. */
let preferredSourceId = null;

/** Set while the renderer is asking for system audio through getDisplayMedia. */
let loopbackAudio = false;

/** True only when this process hid the window in order to take a screenshot. */
let hiddenByCapture = false;

/** Last set of accelerators sent by the renderer, kept for suspend and resume. */
let currentBindings = {};

function createWindow() {
  win = new BrowserWindow({
    width: 1280,
    height: 840,
    minWidth: 960,
    // Tall enough for the whole sidebar, which is why it never scrolls.
    minHeight: 720,
    icon: path.join(__dirname, '..', 'build', 'icon.ico'),
    // Neutral grey avoids a dark flash before the renderer applies the theme.
    backgroundColor: '#1a1d23',
    autoHideMenuBar: true,
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
      // Without this the renderer timers are throttled while the window is
      // minimized, which would stall screenshots and the recording loop.
      backgroundThrottling: false,
    },
  });

  win.once('ready-to-show', () => {
    if (win && !startHidden) win.show();
  });

  // Closing keeps the app alive in the tray, otherwise the Print Screen
  // shortcut would stop working the moment the window is closed.
  // The X hides the window from the taskbar and leaves the app in the tray,
  // where Print Screen keeps working. Minimize is untouched and still sends
  // the window to the taskbar as usual.
  win.on('close', (event) => {
    if (isQuitting) return;
    event.preventDefault();
    if (win) win.hide();
    if (!trayNoticeShown && tray && process.platform === 'win32') {
      trayNoticeShown = true;
      tray.displayBalloon({
        iconType: 'info',
        title: 'Still running in the tray',
        content: 'Print Screen keeps working. Quit from the tray icon menu.',
      });
    }
  });

  // Signing out or shutting down must not be held up by the hide-to-tray
  // behaviour above.
  win.on('session-end', () => {
    isQuitting = true;
  });

  if (isDev) {
    win.loadURL(DEV_URL);
  } else {
    win.loadFile(path.join(__dirname, '..', 'dist', 'index.html'));
  }

  win.on('closed', () => {
    win = null;
  });
}

function showMainWindow() {
  if (!win) return;
  if (win.isMinimized()) win.restore();
  win.show();
  win.focus();
}

function createTray() {
  if (tray) return;
  tray = new Tray(ICON_PATH);
  tray.setToolTip('Proofly');
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: 'Open Proofly', click: showMainWindow },
      {
        label: 'Capture a region',
        click: () => {
          if (win && !win.isDestroyed()) win.webContents.send('shortcut:trigger', 'region');
        },
      },
      { type: 'separator' },
      {
        label: 'Quit',
        click: () => {
          isQuitting = true;
          app.quit();
        },
      },
    ]),
  );
  tray.on('double-click', showMainWindow);
}

// A second launch, for example from the Start menu while the app already
// sits in the tray, brings the running window forward instead of competing
// with it for the global shortcuts.
if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', showMainWindow);
}

app.on('before-quit', () => {
  isQuitting = true;
});

app.whenReady().then(() => {
  // Without an explicit model id Windows groups the process as generic Node and
  // shows its own icon on the taskbar.
  // Windows shows this identifier as the sender of notifications when the app
  // has no Start menu shortcut, which is the case for the zip build.
  app.setAppUserModelId('Proofly');

  // Fallback path: if the legacy getUserMedia constraints ever stop working,
  // the renderer can use getDisplayMedia and this handler picks the screen
  // the user already selected, without showing a second picker.
  session.defaultSession.setDisplayMediaRequestHandler(
    (_request, callback) => {
      desktopCapturer
        .getSources({ types: ['screen'], thumbnailSize: { width: 1, height: 1 } })
        .then((sources) => {
          const chosen = sources.find((s) => s.id === preferredSourceId) || sources[0];
          if (!chosen) {
            callback({});
            return;
          }
          // 'loopback' is what captures what the machine is playing. It is only
          // attached while the renderer explicitly asks for system audio.
          callback(loopbackAudio ? { video: chosen, audio: 'loopback' } : { video: chosen });
        })
        .catch(() => callback({}));
    },
    { useSystemPicker: false },
  );

  // Screen, microphone and system audio requests come from our own renderer.
  session.defaultSession.setPermissionRequestHandler((_webContents, permission, callback) => {
    callback(permission === 'media' || permission === 'display-capture');
  });

  const notifyDisplays = () => {
    if (win && !win.isDestroyed()) win.webContents.send('displays:changed');
  };
  // Resolution changes, docking and unplugging a monitor all land here, so the
  // renderer can refresh the screen list without a restart.
  screen.on('display-metrics-changed', notifyDisplays);
  screen.on('display-added', notifyDisplays);
  screen.on('display-removed', notifyDisplays);

  createWindow();
  createTray();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

/* ------------------------------------------------------------------ */
/* IPC                                                                 */
/* ------------------------------------------------------------------ */

/**
 * List every physical screen together with its native pixel size.
 * Both the desktopCapturer source id (needed by getUserMedia) and the
 * display id are returned, because they are not interchangeable.
 */
ipcMain.handle('sources:list', async () => {
  const displays = screen.getAllDisplays();
  const primaryId = String(screen.getPrimaryDisplay().id);
  const sources = await desktopCapturer.getSources({
    types: ['screen'],
    thumbnailSize: { width: 1, height: 1 },
  });

  return sources.map((s, index) => {
    // display_id is empty on some Windows configurations, fall back to order.
    let display = displays.find((d) => String(d.id) === String(s.display_id));
    if (!display) display = displays[index] || displays[0];
    const scale = display ? display.scaleFactor : 1;
    return {
      id: s.id,
      // s.name is localized by the operating system, so it is ignored here.
      name: `Screen ${index + 1}`,
      displayId: display ? String(display.id) : '',
      width: display ? Math.round(display.size.width * scale) : 1920,
      height: display ? Math.round(display.size.height * scale) : 1080,
      primary: display ? String(display.id) === primaryId : index === 0,
    };
  });
});

/**
 * Pixel perfect screenshot of a single screen at its native resolution.
 * This goes through desktopCapturer instead of the live MediaStream so the
 * result is not resampled by the WebRTC pipeline.
 */
ipcMain.handle('capture:screen', async (_event, payload) => {
  const { sourceId, width, height } = payload || {};
  const sources = await desktopCapturer.getSources({
    types: ['screen'],
    thumbnailSize: {
      width: Math.max(320, Math.round(width || 1920)),
      height: Math.max(200, Math.round(height || 1080)),
    },
  });
  const source = sources.find((s) => s.id === sourceId) || sources[0];
  if (!source) throw new Error('No screen found to capture.');
  const size = source.thumbnail.getSize();
  return {
    data: source.thumbnail.toPNG(),
    width: size.width,
    height: size.height,
  };
});

ipcMain.handle('capture:loopback', (_event, enabled) => {
  loopbackAudio = Boolean(enabled);
  return loopbackAudio;
});

ipcMain.handle('capture:prefer', (_event, sourceId) => {
  preferredSourceId = sourceId || null;
  return true;
});

ipcMain.handle('window:hide', async (_event, displayId) => {
  hiddenByCapture = false;
  if (!win) return false;
  // A minimized window is already off screen, hiding it would only force an
  // unwanted restore afterwards.
  if (win.isMinimized() || !win.isVisible()) return false;
  // If the window sits on a different monitor than the one being captured it
  // cannot show up in the screenshot, so leave it alone.
  if (displayId) {
    const own = screen.getDisplayMatching(win.getBounds());
    if (String(own.id) !== String(displayId)) return false;
  }
  win.hide();
  hiddenByCapture = true;
  // Give the compositor time to actually remove the window before grabbing.
  await new Promise((resolve) => setTimeout(resolve, 220));
  return true;
});

ipcMain.handle('window:minimize', async () => {
  if (!win) return false;
  hiddenByCapture = false;
  win.minimize();
  return true;
});

ipcMain.handle('window:show', async (_event, force) => {
  if (!win) return false;
  if (!force && !hiddenByCapture) return false;
  const wasCaptureHide = hiddenByCapture;
  hiddenByCapture = false;
  if (force) {
    if (win.isMinimized()) win.restore();
    win.show();
    win.focus();
    return true;
  }
  // Restoring after a capture must not steal focus from whatever the user was
  // actually working in. showInactive brings the window back unfocused.
  if (wasCaptureHide) win.showInactive();
  return true;
});

/**
 * Writes the PDF and every recording into one folder chosen by the user.
 * Returns the folder path, or null when the dialog was cancelled.
 */
/** Adds a counter to the file name rather than overwriting an existing export. */
async function uniquePath(candidate) {
  const dir = path.dirname(candidate);
  const ext = path.extname(candidate);
  const stem = path.basename(candidate, ext);
  let attempt = candidate;
  let counter = 2;
  for (;;) {
    try {
      await fs.access(attempt);
    } catch {
      return attempt;
    }
    attempt = path.join(dir, `${stem} (${counter}).${ext.replace(/^\./, '')}`);
    counter += 1;
  }
}

ipcMain.handle('export:bundle', async (_event, payload) => {
  const { pdf, videos, defaultName, targetDir, mode } = payload || {};
  if (!win) throw new Error('The application window is not available.');

  let usableDir = null;
  if (targetDir) {
    try {
      const stat = await fs.stat(targetDir);
      if (stat.isDirectory()) usableDir = targetDir;
    } catch {
      usableDir = null;
    }
  }

  // Recordings only: no PDF is produced, so the user picks a folder rather
  // than a document name.
  if (mode === 'videos') {
    let dir = usableDir;
    if (!dir) {
      const picked = await dialog.showOpenDialog(win, {
        title: 'Choose a folder for the recordings',
        properties: ['openDirectory', 'createDirectory'],
      });
      if (picked.canceled || picked.filePaths.length === 0) return null;
      dir = picked.filePaths[0];
    }
    const paths = [];
    for (let i = 0; i < (videos || []).length; i += 1) {
      const video = videos[i];
      const name = video.name || `recording_${String(i + 1).padStart(2, '0')}.${video.ext || 'webm'}`;
      const target = await uniquePath(path.join(dir, name));
      await fs.writeFile(target, Buffer.from(video.data));
      paths.push(target);
    }
    return { dir, pdfPath: '', videoPaths: paths, usedDefaultFolder: Boolean(usableDir) };
  }

  let pdfPath = null;

  // A configured folder skips the dialog, but only while it is still usable.
  if (usableDir) {
    pdfPath = await uniquePath(path.join(usableDir, defaultName || 'documentation.pdf'));
  }

  if (!pdfPath) {
    const result = await dialog.showSaveDialog(win, {
      title: 'Save documentation',
      defaultPath: defaultName || 'documentation.pdf',
      filters: [{ name: 'PDF', extensions: ['pdf'] }],
    });
    if (result.canceled || !result.filePath) return null;
    pdfPath = result.filePath.toLowerCase().endsWith('.pdf')
      ? result.filePath
      : `${result.filePath}.pdf`;
  }
  const dir = path.dirname(pdfPath);
  const stem = path.basename(pdfPath, '.pdf');

  if (pdf) await fs.writeFile(pdfPath, Buffer.from(pdf));

  const videoPaths = [];
  for (let i = 0; i < (videos || []).length; i += 1) {
    const video = videos[i];
    const ext = video.ext || 'webm';
    const target = await uniquePath(
      path.join(dir, `${stem}_recording_${String(i + 1).padStart(2, '0')}.${ext}`),
    );
    await fs.writeFile(target, Buffer.from(video.data));
    videoPaths.push(target);
  }

  return { dir, pdfPath, videoPaths, usedDefaultFolder: Boolean(usableDir) };
});

function registerBindings(bindings) {
  globalShortcut.unregisterAll();
  const failed = [];
  for (const [action, accelerator] of Object.entries(bindings || {})) {
    if (!accelerator) continue;
    try {
      const ok = globalShortcut.register(accelerator, () => {
        if (win && !win.isDestroyed()) win.webContents.send('shortcut:trigger', action);
      });
      if (!ok) failed.push(accelerator);
    } catch {
      failed.push(accelerator);
    }
  }
  return { failed };
}

ipcMain.handle('shortcuts:apply', (_event, bindings) => {
  currentBindings = bindings || {};
  return registerBindings(currentBindings);
});

/** Used while the user is recording a new key combination in the settings. */
ipcMain.handle('shortcuts:suspend', () => {
  globalShortcut.unregisterAll();
  return true;
});

ipcMain.handle('shortcuts:resume', () => registerBindings(currentBindings));

app.on('will-quit', () => {
  globalShortcut.unregisterAll();
});

/**
 * Opens the folder holding the exported files. shell.showItemInFolder returns
 * nothing and fails silently on Windows, so openPath is used instead: it hands
 * back an error string that can be shown to the user.
 */
/** Numeric comparison, so 1.10.0 is correctly newer than 1.9.0. */
function compareVersions(a, b) {
  const left = String(a).split('.').map((part) => parseInt(part, 10) || 0);
  const right = String(b).split('.').map((part) => parseInt(part, 10) || 0);
  for (let i = 0; i < Math.max(left.length, right.length); i += 1) {
    const diff = (left[i] || 0) - (right[i] || 0);
    if (diff !== 0) return diff > 0 ? 1 : -1;
  }
  return 0;
}

/**
 * Asks GitHub for the latest published release. This runs in the main process
 * because the renderer content policy blocks outside requests, and because
 * net.fetch follows the system proxy settings, which matters on a corporate
 * network.
 */
ipcMain.handle('update:check', async () => {
  const current = app.getVersion();
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 8000);
  try {
    const response = await net.fetch(`https://api.github.com/repos/${REPO}/releases/latest`, {
      headers: {
        Accept: 'application/vnd.github+json',
        'User-Agent': `Proofly/${current}`,
      },
      signal: controller.signal,
    });
    if (response.status === 404) {
      return { current, error: 'No published release was found.' };
    }
    if (!response.ok) {
      return { current, error: `GitHub answered with status ${response.status}.` };
    }
    const data = await response.json();
    const latest = String(data.tag_name || '').replace(/^v/i, '');
    if (!latest) return { current, error: 'The latest release carries no version tag.' };
    return {
      current,
      latest,
      url: typeof data.html_url === 'string' ? data.html_url : '',
      newer: compareVersions(latest, current) > 0,
    };
  } catch (err) {
    const aborted = err && err.name === 'AbortError';
    return { current, error: aborted ? 'The check timed out.' : String(err) };
  } finally {
    clearTimeout(timeout);
  }
});

/** Opens a release page, and only ever a page belonging to this project. */
ipcMain.handle('update:open', async (_event, url) => {
  const fallback = `https://github.com/${REPO}/releases/latest`;
  const safe =
    typeof url === 'string' && url.startsWith(`https://github.com/${REPO}`) ? url : fallback;
  await shell.openExternal(safe);
  return true;
});

/**
 * Freezes one display and lets the user drag a rectangle across it, the way
 * Snipping Tool does. Resolves with the frozen image and the physical pixel
 * rectangle, or null when cancelled.
 */
async function selectRegion(displayId) {
  if (overlay) return null;
  const displays = screen.getAllDisplays();
  const display = displayId
    ? displays.find((d) => String(d.id) === String(displayId)) || screen.getPrimaryDisplay()
    : screen.getDisplayNearestPoint(screen.getCursorScreenPoint());

  const width = Math.round(display.size.width * display.scaleFactor);
  const height = Math.round(display.size.height * display.scaleFactor);
  const sources = await desktopCapturer.getSources({
    types: ['screen'],
    thumbnailSize: { width, height },
  });
  const order = displays.findIndex((d) => d.id === display.id);
  const source =
    sources.find((s) => String(s.display_id) === String(display.id)) ||
    sources[order] ||
    sources[0];
  if (!source) throw new Error('No screen found to capture.');
  const image = source.thumbnail;

  return new Promise((resolve) => {
    let settled = false;
    const view = new BrowserWindow({
      x: display.bounds.x,
      y: display.bounds.y,
      width: display.bounds.width,
      height: display.bounds.height,
      frame: false,
      resizable: false,
      movable: false,
      minimizable: false,
      maximizable: false,
      fullscreenable: false,
      skipTaskbar: true,
      alwaysOnTop: true,
      hasShadow: false,
      enableLargerThanScreen: true,
      show: false,
      backgroundColor: '#000000',
      webPreferences: {
        preload: path.join(__dirname, 'overlay-preload.js'),
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: false,
      },
    });
    overlay = view;
    view.setAlwaysOnTop(true, 'screen-saver');
    view.setBounds(display.bounds);

    const finish = (rect) => {
      if (settled) return;
      settled = true;
      ipcMain.removeListener('overlay:ready', onReady);
      ipcMain.removeListener('overlay:done', onDone);
      ipcMain.removeListener('overlay:cancel', onCancel);
      overlay = null;
      if (!view.isDestroyed()) view.destroy();
      resolve(rect ? { rect, image, display } : null);
    };
    const onReady = (event) => {
      if (event.sender !== view.webContents) return;
      view.show();
      view.focus();
    };
    const onDone = (event, rect) => {
      if (event.sender === view.webContents) finish(rect);
    };
    const onCancel = (event) => {
      if (event.sender === view.webContents) finish(null);
    };

    ipcMain.on('overlay:ready', onReady);
    ipcMain.on('overlay:done', onDone);
    ipcMain.on('overlay:cancel', onCancel);
    view.on('closed', () => finish(null));
    view.webContents.once('did-finish-load', () => {
      view.webContents.send('overlay:image', image.toDataURL());
    });
    view.loadFile(path.join(__dirname, 'overlay.html'));
  });
}

ipcMain.handle('region:select', async (_event, payload) => {
  const { displayId, purpose } = payload || {};
  const result = await selectRegion(displayId || null);
  if (!result) return null;

  const size = result.image.getSize();
  const x = Math.max(0, Math.min(size.width - 1, Math.round(result.rect.x)));
  const y = Math.max(0, Math.min(size.height - 1, Math.round(result.rect.y)));
  const rect = {
    x,
    y,
    w: Math.max(1, Math.min(size.width - x, Math.round(result.rect.w))),
    h: Math.max(1, Math.min(size.height - y, Math.round(result.rect.h))),
  };

  // 'record' and 'lock' only need the rectangle, nothing is cropped or copied.
  if (purpose === 'record' || purpose === 'lock') {
    return { rect, displayId: String(result.display.id) };
  }

  const cropped = result.image.crop({ x: rect.x, y: rect.y, width: rect.w, height: rect.h });
  // Straight into the clipboard, so a region can be pasted anywhere at once.
  clipboard.writeImage(cropped);
  const croppedSize = cropped.getSize();
  return {
    data: cropped.toPNG(),
    width: croppedSize.width,
    height: croppedSize.height,
    displayId: String(result.display.id),
  };
});

/**
 * Start with Windows. The login item points at the running executable, so it
 * only makes sense for the packaged app, and moving the folder breaks it.
 */
const LOGIN_ARGS = ['--hidden'];

ipcMain.handle('autostart:get', () => {
  if (!app.isPackaged) return { available: false, enabled: false };
  return { available: true, enabled: app.getLoginItemSettings({ args: LOGIN_ARGS }).openAtLogin };
});

ipcMain.handle('autostart:set', (_event, enabled) => {
  if (!app.isPackaged) return { available: false, enabled: false };
  app.setLoginItemSettings({ openAtLogin: Boolean(enabled), args: LOGIN_ARGS });
  return { available: true, enabled: app.getLoginItemSettings({ args: LOGIN_ARGS }).openAtLogin };
});

/**
 * Saves one image wherever the user picks. The file type follows the chosen
 * extension, so JPG is converted here and PNG is written as it arrives.
 */
ipcMain.handle('image:save-as', async (_event, payload) => {
  const { data, name } = payload || {};
  if (!win || !data) return null;
  const result = await dialog.showSaveDialog(win, {
    title: 'Save image',
    defaultPath: `${name || 'screenshot'}.png`,
    filters: [
      { name: 'PNG image', extensions: ['png'] },
      { name: 'JPG image', extensions: ['jpg', 'jpeg'] },
    ],
  });
  if (result.canceled || !result.filePath) return null;
  let target = result.filePath;
  if (!/\.(png|jpe?g)$/i.test(target)) target = `${target}.png`;
  const image = nativeImage.createFromBuffer(Buffer.from(data));
  const bytes = /\.jpe?g$/i.test(target) ? image.toJPEG(92) : image.toPNG();
  await fs.writeFile(target, bytes);
  return target;
});

/** Copies an already cropped image, used by captures of a locked region. */
ipcMain.handle('clipboard:write-image', (_event, data) => {
  if (!data) return false;
  const image = nativeImage.createFromBuffer(Buffer.from(data));
  if (image.isEmpty()) return false;
  clipboard.writeImage(image);
  return true;
});

ipcMain.handle('shell:reveal', async (_event, target) => {
  if (!target) return { ok: false, error: 'No path to open.' };
  const full = path.normalize(String(target));
  let dir = full;
  try {
    const stat = await fs.stat(full);
    if (!stat.isDirectory()) dir = path.dirname(full);
  } catch {
    dir = path.dirname(full);
  }
  try {
    const error = await shell.openPath(dir);
    return error ? { ok: false, error } : { ok: true };
  } catch (err) {
    return { ok: false, error: String(err) };
  }
});

/** Folder picker for the optional default export location. */
ipcMain.handle('dialog:choose-folder', async () => {
  if (!win) return null;
  const result = await dialog.showOpenDialog(win, {
    title: 'Choose the default save folder',
    properties: ['openDirectory', 'createDirectory'],
  });
  if (result.canceled || result.filePaths.length === 0) return null;
  return result.filePaths[0];
});
