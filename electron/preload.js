'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  listSources: () => ipcRenderer.invoke('sources:list'),
  captureScreen: (sourceId, width, height) =>
    ipcRenderer.invoke('capture:screen', { sourceId, width, height }),
  setPreferredSource: (sourceId) => ipcRenderer.invoke('capture:prefer', sourceId),
  setLoopbackAudio: (enabled) => ipcRenderer.invoke('capture:loopback', enabled),
  hideWindow: (displayId) => ipcRenderer.invoke('window:hide', displayId || null),
  showWindow: (force) => ipcRenderer.invoke('window:show', Boolean(force)),
  minimizeWindow: () => ipcRenderer.invoke('window:minimize'),
  applyShortcuts: (bindings) => ipcRenderer.invoke('shortcuts:apply', bindings),
  suspendShortcuts: () => ipcRenderer.invoke('shortcuts:suspend'),
  resumeShortcuts: () => ipcRenderer.invoke('shortcuts:resume'),
  onDisplaysChanged: (callback) => {
    const listener = () => callback();
    ipcRenderer.on('displays:changed', listener);
    return () => ipcRenderer.removeListener('displays:changed', listener);
  },
  onShortcut: (callback) => {
    const listener = (_event, action) => callback(action);
    ipcRenderer.on('shortcut:trigger', listener);
    return () => ipcRenderer.removeListener('shortcut:trigger', listener);
  },
  exportBundle: (pdf, videos, defaultName, targetDir, mode) =>
    ipcRenderer.invoke('export:bundle', {
      pdf,
      videos,
      defaultName,
      targetDir: targetDir || null,
      mode: mode || 'bundle',
    }),
  chooseFolder: () => ipcRenderer.invoke('dialog:choose-folder'),
  reveal: (filePath) => ipcRenderer.invoke('shell:reveal', filePath),
  selectRegion: (displayId, purpose) =>
    ipcRenderer.invoke('region:select', { displayId: displayId || null, purpose }),
  copyImage: (data) => ipcRenderer.invoke('clipboard:write-image', data),
  saveImageAs: (data, name) => ipcRenderer.invoke('image:save-as', { data, name }),
  getAutostart: () => ipcRenderer.invoke('autostart:get'),
  setAutostart: (enabled) => ipcRenderer.invoke('autostart:set', Boolean(enabled)),
  checkUpdate: () => ipcRenderer.invoke('update:check'),
  openRelease: (url) => ipcRenderer.invoke('update:open', url),
});
