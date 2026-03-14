#!/usr/bin/env node
import { execute } from '@oclif/core';
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { shouldPrintVersion, shouldSelfUpdate } from './run-helpers.js';

const args = process.argv.slice(2);
const require = createRequire(import.meta.url);
const pkg = require('../package.json');
const packageName = pkg.name ?? '@agon_agents/cli';

if (shouldPrintVersion(args)) {
  console.log(pkg.version);
  process.exit(0);
}

if (shouldSelfUpdate(args)) {
  runNpmGlobalInstall(`${packageName}@latest`)
    .then(() => {
      console.log(`Updated ${packageName} to latest.`);
      process.exit(0);
    })
    .catch((error) => {
      const message = error instanceof Error ? error.message : String(error);
      console.error(`Self-update failed: ${message}`);
      process.exit(1);
    });
} else {
execute({ development: false, dir: import.meta.url })
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
}

function runNpmGlobalInstall(target) {
  return new Promise((resolve, reject) => {
    const child = spawn('npm', ['install', '-g', target], {
      stdio: 'inherit',
      shell: process.platform === 'win32'
    });

    child.on('error', (error) => {
      reject(new Error(`Unable to start npm: ${error.message}`));
    });

    child.on('exit', (code) => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(new Error(`npm install exited with status ${code ?? 'unknown'}`));
    });
  });
}
