'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('overlay', {
  onImage: (callback) => ipcRenderer.on('overlay:image', (_event, url) => callback(url)),
  ready: () => ipcRenderer.send('overlay:ready'),
  done: (rect) => ipcRenderer.send('overlay:done', rect),
  cancel: () => ipcRenderer.send('overlay:cancel'),
});
