#!/usr/bin/env node
'use strict';

const childProcess = require('child_process');
const fs = require('fs');
const path = require('path');

const packageRoot = path.resolve(__dirname, '..');
const SUPPORTED_RUNTIMES = new Set(['win-x64', 'linux-x64', 'osx-x64', 'osx-arm64']);
const runtime = validateRuntime(process.env.SELENIUM_PW_MIGRATOR_RUNTIME || resolveRuntime());
const exeName = runtime.startsWith('win-') ? 'selenium-pw-migrator.exe' : 'selenium-pw-migrator';
const explicitInstallDir = process.env.SELENIUM_PW_MIGRATOR_INSTALL_DIR || '';
const binaryPath = explicitInstallDir
  ? path.join(explicitInstallDir, exeName)
  : path.join(packageRoot, 'native', runtime, exeName);

if (!fs.existsSync(binaryPath)) {
  const installer = path.join(packageRoot, 'scripts', 'install.js');
  const installResult = childProcess.spawnSync(process.execPath, [installer], { stdio: 'inherit' });
  if (installResult.error) throw installResult.error;
  if (installResult.status !== 0) process.exit(installResult.status || 1);
}

const result = childProcess.spawnSync(binaryPath, process.argv.slice(2), { stdio: 'inherit' });
if (result.error) {
  console.error(`[selenium-pw-migrator] Failed to start native CLI: ${result.error.message}`);
  process.exit(1);
}

if (typeof result.status === 'number') {
  process.exit(result.status);
}

process.exit(result.signal ? 1 : 0);

function validateRuntime(value) {
  if (!SUPPORTED_RUNTIMES.has(value)) {
    throw new Error(`Unsupported runtime: ${value}. Supported runtimes: ${Array.from(SUPPORTED_RUNTIMES).join(', ')}`);
  }
  return value;
}

function resolveRuntime() {
  const platform = process.platform;
  const arch = process.arch;

  let osPart;
  if (platform === 'win32') osPart = 'win';
  else if (platform === 'linux') osPart = 'linux';
  else if (platform === 'darwin') osPart = 'osx';
  else throw new Error(`Unsupported platform: ${platform}`);

  let archPart;
  if (arch === 'x64') archPart = 'x64';
  else if (arch === 'arm64') archPart = 'arm64';
  else throw new Error(`Unsupported architecture: ${arch}`);

  return `${osPart}-${archPart}`;
}
