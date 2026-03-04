"use strict";
const electron = require("electron");
const path = require("node:path");
const fs = require("node:fs");
const IMAGE_EXTENSIONS = /* @__PURE__ */ new Set([
  ".png",
  ".jpg",
  ".jpeg",
  ".bmp",
  ".gif",
  ".webp",
  ".tiff",
  ".tif"
]);
const MIME_MAP = {
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".bmp": "image/bmp",
  ".gif": "image/gif",
  ".webp": "image/webp",
  ".tiff": "image/tiff",
  ".tif": "image/tiff"
};
function createWindow() {
  const mainWindow = new electron.BrowserWindow({
    width: 1300,
    height: 800,
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false
    }
  });
  {
    mainWindow.loadURL("http://localhost:5173");
  }
}
electron.ipcMain.handle("dialog:openFile", async (_event, filters) => {
  const result = await electron.dialog.showOpenDialog({
    properties: ["openFile"],
    filters: filters || [{ name: "翻译文件", extensions: ["txt"] }]
  });
  return result.canceled ? null : result.filePaths[0];
});
electron.ipcMain.handle("dialog:saveFile", async (_event, defaultName, filters) => {
  const result = await electron.dialog.showSaveDialog({
    defaultPath: defaultName,
    filters: filters || [{ name: "翻译文件", extensions: ["txt"] }]
  });
  return result.canceled ? null : result.filePath;
});
electron.ipcMain.handle("dialog:selectDirectory", async () => {
  const result = await electron.dialog.showOpenDialog({
    properties: ["openDirectory"]
  });
  return result.canceled ? null : result.filePaths[0];
});
electron.ipcMain.handle("fs:readTextFile", async (_event, filePath) => {
  return fs.promises.readFile(filePath, "utf-8");
});
electron.ipcMain.handle("fs:writeTextFile", async (_event, filePath, content) => {
  const dir = path.dirname(filePath);
  await fs.promises.mkdir(dir, { recursive: true });
  await fs.promises.writeFile(filePath, content, "utf-8");
});
electron.ipcMain.handle("fs:listImages", async (_event, dirPath) => {
  try {
    const entries = await fs.promises.readdir(dirPath);
    return entries.filter((name) => IMAGE_EXTENSIONS.has(path.extname(name).toLowerCase())).sort();
  } catch (err) {
    const code = err.code;
    if (code === "ENOENT") {
      console.warn(`[图片列表] 目录不存在: ${dirPath}`);
      return [];
    }
    throw err;
  }
});
electron.ipcMain.handle("fs:readImageAsDataUrl", async (_event, filePath) => {
  try {
    const buffer = await fs.promises.readFile(filePath);
    const ext = path.extname(filePath).toLowerCase();
    const mime = MIME_MAP[ext] || "image/png";
    return `data:${mime};base64,${buffer.toString("base64")}`;
  } catch (err) {
    const code = err.code;
    if (code === "ENOENT") {
      console.warn(`[图片加载] 文件不存在: ${filePath}`);
      return null;
    }
    throw err;
  }
});
electron.ipcMain.handle("shell:openInExplorer", async (_event, dirPath) => {
  await electron.shell.openPath(dirPath);
});
electron.ipcMain.handle("path:dirname", (_event, filePath) => {
  return path.dirname(filePath);
});
electron.ipcMain.handle("path:join", (_event, ...parts) => {
  return path.join(...parts);
});
electron.app.whenReady().then(() => {
  createWindow();
  electron.app.on("activate", () => {
    if (electron.BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});
electron.app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    electron.app.quit();
  }
});
