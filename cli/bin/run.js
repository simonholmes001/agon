#!/usr/bin/env node
import { execute } from '@oclif/core';
import { createRequire } from 'node:module';
import { shouldPrintVersion } from './run-helpers.js';

const args = process.argv.slice(2);

if (shouldPrintVersion(args)) {
  const require = createRequire(import.meta.url);
  const pkg = require('../package.json');
  console.log(pkg.version);
  process.exit(0);
}

execute({ development: false, dir: import.meta.url })
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
