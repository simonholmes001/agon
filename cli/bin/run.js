#!/usr/bin/env node
import { execute } from '@oclif/core';
import { createRequire } from 'node:module';
import {
  buildTopLevelHelp,
  shouldPrintTopLevelHelp,
  shouldPrintVersion,
  shouldSelfUpdate
} from './run-helpers.js';

const args = process.argv.slice(2);
const require = createRequire(import.meta.url);
const pkg = require('../package.json');

if (shouldPrintVersion(args)) {
  console.log(pkg.version);
  process.exit(0);
}

if (shouldPrintTopLevelHelp(args)) {
  const binName = pkg.oclif?.bin ?? 'agon';
  console.log(buildTopLevelHelp(binName));
  process.exit(0);
}

const normalizedArgs = normalizeSelfUpdateArgs(args);
if (normalizedArgs !== args) {
  process.argv = [process.argv[0], process.argv[1], ...normalizedArgs];
}

execute({ development: false, dir: import.meta.url })
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });

function normalizeSelfUpdateArgs(cliArgs) {
  if (!shouldSelfUpdate(cliArgs) || !cliArgs.includes('--self-update')) {
    return cliArgs;
  }

  const forwardedArgs = cliArgs.filter(arg => arg !== '--self-update');
  return ['self-update', ...forwardedArgs];
}
