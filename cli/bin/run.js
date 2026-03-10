#!/usr/bin/env node
import { execute } from '@oclif/core';

execute({ development: false, dir: import.meta.url })
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
