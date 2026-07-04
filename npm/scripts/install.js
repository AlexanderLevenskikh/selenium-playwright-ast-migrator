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
const skipDownload = isTruthy(readOption('SELENIUM_PW_MIGRATOR_SKIP_DOWNLOAD', 'selenium-pw-migrator-skip-download'));
const version = readOption('SELENIUM_PW_MIGRATOR_VERSION', 'selenium-pw-migrator-version') || packageJson.version;
const runtime = validateRuntime(readOption('SELENIUM_PW_MIGRATOR_RUNTIME', 'selenium-pw-migrator-runtime') || resolveRuntime());
const baseUrlOverride = readOption('SELENIUM_PW_MIGRATOR_BASE_URL', 'selenium-pw-migrator-base-url');
const baseUrl = normalizeBaseUrl(baseUrlOverride || `https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v${version}`);
const archivePathOverride = readOption('SELENIUM_PW_MIGRATOR_ARCHIVE_PATH', 'selenium-pw-migrator-archive-path') || '';
const checksumsPathOverride = readOption('SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH', 'selenium-pw-migrator-checksums-path') || '';
const installDir = readOption('SELENIUM_PW_MIGRATOR_INSTALL_DIR', 'selenium-pw-migrator-install-dir') || path.join(packageRoot, 'native', runtime);
const archiveName = resolveArchiveName(version, runtime, archivePathOverride);

main().catch(error => {
  console.error(`[selenium-pw-migrator] ${error.message}`);
  process.exit(1);
});

async function main() {
  if (skipDownload) {
    console.log('[selenium-pw-migrator] Native download skipped because SELENIUM_PW_MIGRATOR_SKIP_DOWNLOAD or npm config selenium-pw-migrator-skip-download is set.');
    return;
  }

  if (baseUrlOverride) {
    console.log(`[selenium-pw-migrator] Using configured standalone release base URL: ${baseUrl}`);
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


function readOption(envName, npmConfigName) {
  const envValue = env[envName];
  if (hasValue(envValue)) return envValue;

  for (const npmEnvName of npmConfigEnvNames(npmConfigName)) {
    const npmValue = env[npmEnvName];
    if (hasValue(npmValue)) return npmValue;
  }

  return '';
}

function npmConfigEnvNames(configName) {
  const normalized = String(configName).replace(/[^A-Za-z0-9]/g, '_');
  return [
    `npm_config_${normalized}`,
    `NPM_CONFIG_${normalized.toUpperCase()}`
  ];
}

function hasValue(value) {
  return value !== undefined && value !== null && String(value).length > 0;
}

function isTruthy(value) {
  return value === '1' || String(value).toLowerCase() === 'true' || String(value).toLowerCase() === 'yes';
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
