#!/usr/bin/env node
'use strict';

const childProcess = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const http = require('http');
const https = require('https');
const os = require('os');
const path = require('path');

const packageRoot = path.resolve(__dirname, '..');
const packageJson = require(path.join(packageRoot, 'package.json'));
const SUPPORTED_RUNTIMES = new Set(['win-x64', 'linux-x64', 'osx-x64', 'osx-arm64']);

const env = process.env;
const skipDownload = env.SELENIUM_PW_MIGRATOR_SKIP_DOWNLOAD === '1' || env.SELENIUM_PW_MIGRATOR_SKIP_DOWNLOAD === 'true';
const version = env.SELENIUM_PW_MIGRATOR_VERSION || packageJson.version;
const runtime = validateRuntime(env.SELENIUM_PW_MIGRATOR_RUNTIME || resolveRuntime());
const baseUrl = normalizeBaseUrl(env.SELENIUM_PW_MIGRATOR_BASE_URL || `https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v${version}`);
const archivePathOverride = env.SELENIUM_PW_MIGRATOR_ARCHIVE_PATH || '';
const checksumsPathOverride = env.SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH || '';
const installDir = env.SELENIUM_PW_MIGRATOR_INSTALL_DIR || path.join(packageRoot, 'native', runtime);
const archiveName = resolveArchiveName(version, runtime, archivePathOverride);

main().catch(error => {
  console.error(`[selenium-pw-migrator] ${error.message}`);
  process.exit(1);
});

async function main() {
  if (skipDownload) {
    console.log('[selenium-pw-migrator] Native download skipped because SELENIUM_PW_MIGRATOR_SKIP_DOWNLOAD is set.');
    return;
  }

  fs.rmSync(installDir, { recursive: true, force: true });
  fs.mkdirSync(installDir, { recursive: true });

  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'selenium-pw-migrator-npm-'));
  try {
    const archivePath = archivePathOverride
      ? path.resolve(archivePathOverride)
      : path.join(tempDir, archiveName);

    if (archivePathOverride) {
      if (!fs.existsSync(archivePath)) {
        throw new Error(`SELENIUM_PW_MIGRATOR_ARCHIVE_PATH was not found: ${archivePath}`);
      }
      console.log(`[selenium-pw-migrator] Using local archive: ${archivePath}`);
    } else {
      const archiveUrl = `${baseUrl}/${archiveName}`;
      console.log(`[selenium-pw-migrator] Downloading ${archiveUrl}`);
      await downloadFile(archiveUrl, archivePath);
    }

    const checksumsPath = checksumsPathOverride
      ? path.resolve(checksumsPathOverride)
      : path.join(tempDir, 'checksums.sha256');

    if (checksumsPathOverride) {
      verifyChecksum(archivePath, checksumsPath, path.basename(archivePath));
    } else {
      try {
        await downloadFile(`${baseUrl}/checksums.sha256`, checksumsPath);
        verifyChecksum(archivePath, checksumsPath, archiveName);
      } catch (error) {
        console.warn(`[selenium-pw-migrator] Checksum verification skipped: ${error.message}`);
      }
    }

    const extractDir = path.join(tempDir, 'extract');
    fs.mkdirSync(extractDir, { recursive: true });
    extractArchive(archivePath, extractDir, runtime);
    copyDirectoryContents(extractDir, installDir);
    ensureExecutable(path.join(installDir, executableName(runtime)));

    console.log(`[selenium-pw-migrator] Installed native CLI to ${installDir}`);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
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

function validateRuntime(value) {
  if (!SUPPORTED_RUNTIMES.has(value)) {
    throw new Error(`Unsupported runtime: ${value}. Supported runtimes: ${Array.from(SUPPORTED_RUNTIMES).join(', ')}`);
  }
  return value;
}

function resolveArchiveName(packageVersion, rid, explicitArchivePath) {
  if (explicitArchivePath) return path.basename(explicitArchivePath);
  const extension = rid.startsWith('win-') ? '.zip' : '.tar.gz';
  return `selenium-pw-migrator-${packageVersion}-${rid}${extension}`;
}

function normalizeBaseUrl(value) {
  return String(value).replace(/\/+$/, '');
}

function executableName(rid) {
  return rid.startsWith('win-') ? 'selenium-pw-migrator.exe' : 'selenium-pw-migrator';
}

function downloadFile(url, destination) {
  return new Promise((resolve, reject) => {
    const client = url.startsWith('https:') ? https : http;
    const request = client.get(url, response => {
      if (response.statusCode >= 300 && response.statusCode < 400 && response.headers.location) {
        response.resume();
        downloadFile(new URL(response.headers.location, url).toString(), destination).then(resolve, reject);
        return;
      }

      if (response.statusCode !== 200) {
        response.resume();
        reject(new Error(`HTTP ${response.statusCode} while downloading ${url}`));
        return;
      }

      const file = fs.createWriteStream(destination);
      response.pipe(file);
      file.on('finish', () => file.close(resolve));
      file.on('error', reject);
    });

    request.on('error', reject);
  });
}

function verifyChecksum(archivePath, checksumsPath, expectedArchiveName) {
  if (!fs.existsSync(checksumsPath)) {
    throw new Error(`Checksums file was not found: ${checksumsPath}`);
  }

  const checksums = fs.readFileSync(checksumsPath, 'utf8').split(/\r?\n/);
  const escaped = escapeRegExp(expectedArchiveName);
  const line = checksums.find(value => new RegExp(`\\s${escaped}$`).test(value.trim()));
  if (!line) {
    throw new Error(`No checksum entry for ${expectedArchiveName} in ${checksumsPath}`);
  }

  const expected = line.trim().split(/\s+/)[0].toLowerCase();
  const actual = crypto.createHash('sha256').update(fs.readFileSync(archivePath)).digest('hex').toLowerCase();
  if (expected !== actual) {
    throw new Error(`Checksum mismatch for ${expectedArchiveName}. Expected ${expected}, actual ${actual}.`);
  }

  console.log('[selenium-pw-migrator] Checksum verified.');
}

function extractArchive(archivePath, destination, rid) {
  if (rid.startsWith('win-')) {
    const command = [
      '$ErrorActionPreference = "Stop";',
      'Expand-Archive',
      '-LiteralPath', quotePowerShell(archivePath),
      '-DestinationPath', quotePowerShell(destination),
      '-Force'
    ].join(' ');
    run('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', command]);
    return;
  }

  run('tar', ['-xzf', archivePath, '-C', destination]);
}

function run(command, args) {
  const result = childProcess.spawnSync(command, args, { stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} ${args.join(' ')} failed with exit code ${result.status}`);
}

function copyDirectoryContents(source, target) {
  for (const entry of fs.readdirSync(source)) {
    fs.cpSync(path.join(source, entry), path.join(target, entry), { recursive: true, force: true });
  }
}

function ensureExecutable(filePath) {
  if (fs.existsSync(filePath) && process.platform !== 'win32') {
    fs.chmodSync(filePath, 0o755);
  }
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function quotePowerShell(value) {
  return `'${String(value).replace(/'/g, "''")}'`;
}
