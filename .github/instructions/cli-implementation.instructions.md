---
applyTo: 'cli/**'
---
# Agon CLI Implementation Guide

**Version:** 1.0  
**Phase:** 1 (MVP)

---

## 1) Overview

The Agon CLI is a terminal-based interface for running AI-powered strategy debates. It targets developers, product managers, and technical founders who are comfortable in the terminal and value speed over visual polish.

**Design Philosophy:**
- **Text-first:** All artifacts are Markdown, rendered beautifully in the terminal
- **Progressive disclosure:** Start simple (one command), reveal complexity as needed
- **Respectful of user time:** Show progress, don't block unnecessarily
- **Fail gracefully:** Clear error messages with recovery suggestions

---

## 2) Technology Stack

### Core Dependencies

```json
{
  "dependencies": {
    "@oclif/core": "^3.0.0",           // CLI framework
    "@oclif/plugin-help": "^6.0.0",     // Auto-generated help
    "@oclif/plugin-plugins": "^4.0.0",  // Plugin system
    "ink": "^4.0.0",                    // React for terminal UI
    "react": "^18.0.0",                 // Required by ink
    "axios": "^1.6.0",                  // HTTP client
    "eventsource": "^2.0.0",            // SSE for streaming
    "marked": "^11.0.0",                // Markdown parser
    "marked-terminal": "^7.0.0",        // Terminal markdown renderer
    "chalk": "^5.0.0",                  // Terminal colors
    "ora": "^8.0.0",                    // Spinner for loading states
    "inquirer": "^9.0.0",               // Interactive prompts
    "cli-table3": "^0.6.0",             // Tables for lists
    "boxen": "^7.0.0",                  // Boxes for important messages
    "cosmiconfig": "^9.0.0",            // Config file support
    "date-fns": "^3.0.0",               // Date formatting
    "zod": "^3.22.0"                    // Runtime validation
  },
  "devDependencies": {
    "@types/node": "^20.0.0",
    "@types/react": "^18.0.0",
    "@types/marked": "^6.0.0",
    "@types/inquirer": "^9.0.0",
    "typescript": "^5.3.0",
    "ts-node": "^10.9.0",
    "vitest": "^1.0.0",
    "@oclif/test": "^3.0.0"
  }
}
```

### Why oclif?

1. **Battle-tested:** Used by Heroku CLI, Salesforce CLI, GitHub CLI (`gh`)
2. **TypeScript-native:** Full type safety out of the box
3. **Auto-help:** Generates help text from command definitions
4. **Plugin system:** Easy to extend and modularize
5. **Testing utilities:** Built-in mocking and assertion helpers

### Why ink?

1. **React patterns:** Use familiar component model in terminal
2. **State management:** Hooks work the same as in React web apps
3. **Layout primitives:** Flexbox for terminal UIs
4. **Color and style:** Full styling support with chalk integration

---

## 3) Project Structure

```
cli/
├── bin/
│   ├── run.js                       # Main entry point (symlink to agon)
│   └── dev.js                       # Development entry point
├── src/
│   ├── commands/                    # All CLI commands
│   │   ├── start.ts                # agon start <idea>
│   │   ├── clarify.ts              # agon clarify (interactive)
│   │   ├── status.ts               # agon status
│   │   ├── show.ts                 # agon show <artifact>
│   │   ├── challenge.ts            # agon challenge <claim-id>
│   │   ├── constraint.ts           # agon constraint <text>
│   │   ├── deepdive.ts             # agon deepdive <entity-id>
│   │   ├── sessions/
│   │   │   ├── index.ts            # agon sessions (list)
│   │   │   ├── resume.ts           # agon sessions resume <id>
│   │   │   └── fork.ts             # agon sessions fork <id>
│   │   └── config/
│   │       ├── index.ts            # agon config (show)
│   │       └── set.ts              # agon config set <key> <value>
│   ├── api/
│   │   ├── client.ts               # AgonAPIClient (axios wrapper)
│   │   ├── types.ts                # API types (generated from backend)
│   │   ├── session-service.ts      # Session API methods
│   │   ├── artifact-service.ts     # Artifact API methods
│   │   └── hitl-service.ts         # HITL API methods
│   ├── ui/
│   │   ├── components/
│   │   │   ├── Spinner.tsx         # Loading spinner
│   │   │   ├── ProgressBar.tsx     # Progress indicator
│   │   │   ├── StatusDisplay.tsx   # Session status component
│   │   │   ├── QuestionPrompt.tsx  # Clarification Q&A
│   │   │   └── ErrorBox.tsx        # Error display
│   │   ├── hooks/
│   │   │   ├── useSession.ts       # Session state hook
│   │   │   └── usePolling.ts       # Polling hook for status updates
│   │   └── renderers/
│   │       ├── markdown.ts         # Markdown to terminal
│   │       ├── table.ts            # Data table renderer
│   │       └── box.ts              # Boxed messages
│   ├── state/
│   │   ├── session-manager.ts      # Manages ~/.agon/sessions/
│   │   ├── config-manager.ts       # Manages ~/.agonrc
│   │   └── cache-manager.ts        # API response caching
│   ├── utils/
│   │   ├── logger.ts               # Structured logging
│   │   ├── error-handler.ts        # Error formatting
│   │   ├── formatter.ts            # Data formatters
│   │   └── validation.ts           # Input validation (Zod schemas)
│   └── index.ts                     # Export all commands
├── test/
│   ├── commands/
│   ├── api/
│   ├── ui/
│   └── helpers/
│       └── mock-api.ts
├── docs/
│   ├── commands.md                  # Command reference
│   └── configuration.md             # Configuration guide
├── .agonrc.example                  # Example config file
├── package.json
├── tsconfig.json
├── vitest.config.ts
└── README.md
```

---

## 4) Command Design Patterns

### 4.1 Command Class Structure (oclif)

```typescript
import { Command, Flags } from '@oclif/core';
import { AgonAPIClient } from '../../api/client';

export default class Start extends Command {
  static description = 'Start a new strategy debate session';
  
  static examples = [
    '<%= config.bin %> start "Build a SaaS for project management"',
    '<%= config.bin %> start "Launch a mobile app" --friction 85',
    '<%= config.bin %> start "Redesign checkout flow" --no-research',
  ];
  
  static flags = {
    friction: Flags.integer({
      char: 'f',
      description: 'Friction level (0-100)',
      default: 50,
    }),
    research: Flags.boolean({
      description: 'Enable research tools',
      default: true,
      allowNo: true,
    }),
    interactive: Flags.boolean({
      char: 'i',
      description: 'Interactive mode (step-by-step)',
      default: true,
    }),
  };
  
  static args = [
    {
      name: 'idea',
      required: true,
      description: 'The idea or decision to analyze',
    },
  ];

  async run(): Promise<void> {
    const { args, flags } = await this.parse(Start);
    
    // Command implementation
    const api = new AgonAPIClient(this.config.configDir);
    
    // ... rest of implementation
  }
}
```

### 4.2 API Client Pattern

```typescript
// src/api/client.ts
import axios, { AxiosInstance } from 'axios';
import { ConfigManager } from '../state/config-manager';

export class AgonAPIClient {
  private client: AxiosInstance;
  private config: ConfigManager;

  constructor(configDir: string) {
    this.config = new ConfigManager(configDir);
    
    this.client = axios.create({
      baseURL: this.config.get('apiUrl') || 'http://localhost:5000',
      timeout: 30000,
      headers: {
        'Content-Type': 'application/json',
      },
    });
    
    // Add request/response interceptors for logging
    this.client.interceptors.request.use(this.logRequest);
    this.client.interceptors.response.use(this.logResponse, this.handleError);
  }

  async createSession(idea: string, frictionLevel: number): Promise<Session> {
    const response = await this.client.post('/sessions', {
      idea,
      frictionLevel,
    });
    return response.data;
  }

  async getSession(sessionId: string): Promise<Session> {
    const response = await this.client.get(`/sessions/${sessionId}`);
    return response.data;
  }

  // ... more methods
}
```

### 4.3 ink UI Component Pattern

```typescript
// src/ui/components/StatusDisplay.tsx
import React, { FC } from 'react';
import { Box, Text } from 'ink';
import Spinner from 'ink-spinner';

interface Props {
  phase: string;
  status: string;
  convergence?: number;
  isLoading: boolean;
}

export const StatusDisplay: FC<Props> = ({ phase, status, convergence, isLoading }) => {
  return (
    <Box flexDirection="column" marginY={1}>
      <Box>
        <Text bold>Phase: </Text>
        <Text color="blue">{phase}</Text>
        {isLoading && (
          <Box marginLeft={1}>
            <Text color="gray">
              <Spinner type="dots" />
            </Text>
          </Box>
        )}
      </Box>
      
      <Box>
        <Text bold>Status: </Text>
        <Text color={status === 'Active' ? 'green' : 'yellow'}>{status}</Text>
      </Box>
      
      {convergence !== undefined && (
        <Box>
          <Text bold>Convergence: </Text>
          <Text color={convergence >= 0.75 ? 'green' : 'yellow'}>
            {(convergence * 100).toFixed(0)}%
          </Text>
        </Box>
      )}
    </Box>
  );
};
```

### 4.4 Polling Pattern

```typescript
// src/ui/hooks/usePolling.ts
import { useState, useEffect, useRef } from 'react';
import { AgonAPIClient } from '../../api/client';

export function usePolling(
  sessionId: string,
  api: AgonAPIClient,
  interval: number = 2000
) {
  const [session, setSession] = useState<Session | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const intervalRef = useRef<NodeJS.Timeout>();

  useEffect(() => {
    const poll = async () => {
      try {
        const data = await api.getSession(sessionId);
        setSession(data);
        
        // Stop polling if session is complete
        if (data.status === 'Complete' || data.status === 'CompleteWithGaps') {
          if (intervalRef.current) {
            clearInterval(intervalRef.current);
          }
        }
      } catch (err) {
        setError(err as Error);
      }
    };

    // Poll immediately
    poll();
    
    // Then poll on interval
    intervalRef.current = setInterval(poll, interval);

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
      }
    };
  }, [sessionId, api, interval]);

  return { session, error };
}
```

---

## 5) Local State Management

### 5.1 Directory Structure

```
~/.agon/
├── config.yaml                      # User config
├── current-session                  # File containing active session ID
├── sessions/
│   ├── <session-id-1>.json         # Cached session state
│   ├── <session-id-2>.json
│   └── ...
├── artifacts/
│   └── <session-id>/
│       ├── verdict.md
│       ├── plan.md
│       └── ...
└── logs/
    ├── agon.log                     # Debug logs
    └── api.log                      # API request/response logs
```

### 5.2 Session Manager

```typescript
// src/state/session-manager.ts
import fs from 'fs-extra';
import path from 'path';
import os from 'os';

export class SessionManager {
  private baseDir: string;

  constructor() {
    this.baseDir = path.join(os.homedir(), '.agon');
    this.ensureDirectories();
  }

  private ensureDirectories(): void {
    fs.ensureDirSync(path.join(this.baseDir, 'sessions'));
    fs.ensureDirSync(path.join(this.baseDir, 'artifacts'));
    fs.ensureDirSync(path.join(this.baseDir, 'logs'));
  }

  getCurrentSessionId(): string | null {
    const currentSessionFile = path.join(this.baseDir, 'current-session');
    if (fs.existsSync(currentSessionFile)) {
      return fs.readFileSync(currentSessionFile, 'utf-8').trim();
    }
    return null;
  }

  setCurrentSessionId(sessionId: string): void {
    const currentSessionFile = path.join(this.baseDir, 'current-session');
    fs.writeFileSync(currentSessionFile, sessionId);
  }

  cacheSession(session: Session): void {
    const sessionFile = path.join(this.baseDir, 'sessions', `${session.sessionId}.json`);
    fs.writeJsonSync(sessionFile, session, { spaces: 2 });
  }

  getCachedSession(sessionId: string): Session | null {
    const sessionFile = path.join(this.baseDir, 'sessions', `${sessionId}.json`);
    if (fs.existsSync(sessionFile)) {
      return fs.readJsonSync(sessionFile);
    }
    return null;
  }

  listSessions(): Session[] {
    const sessionsDir = path.join(this.baseDir, 'sessions');
    const files = fs.readdirSync(sessionsDir).filter(f => f.endsWith('.json'));
    
    return files.map(file => {
      const sessionFile = path.join(sessionsDir, file);
      return fs.readJsonSync(sessionFile);
    }).sort((a, b) => {
      return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
    });
  }

  cacheArtifact(sessionId: string, artifactType: string, content: string): void {
    const artifactDir = path.join(this.baseDir, 'artifacts', sessionId);
    fs.ensureDirSync(artifactDir);
    
    const artifactFile = path.join(artifactDir, `${artifactType}.md`);
    fs.writeFileSync(artifactFile, content);
  }

  getCachedArtifact(sessionId: string, artifactType: string): string | null {
    const artifactFile = path.join(this.baseDir, 'artifacts', sessionId, `${artifactType}.md`);
    if (fs.existsSync(artifactFile)) {
      return fs.readFileSync(artifactFile, 'utf-8');
    }
    return null;
  }
}
```

### 5.3 Config Manager

```typescript
// src/state/config-manager.ts
import { cosmiconfig } from 'cosmiconfig';
import fs from 'fs-extra';
import path from 'path';
import os from 'os';

export interface AgonConfig {
  apiUrl: string;
  defaultFriction: number;
  researchEnabled: boolean;
  logLevel: 'debug' | 'info' | 'warn' | 'error';
}

const DEFAULT_CONFIG: AgonConfig = {
  apiUrl: 'http://localhost:5000',
  defaultFriction: 50,
  researchEnabled: true,
  logLevel: 'info',
};

export class ConfigManager {
  private configPath: string;
  private config: AgonConfig;

  constructor(configDir?: string) {
    this.configPath = path.join(configDir || os.homedir(), '.agonrc');
    this.config = this.load();
  }

  private load(): AgonConfig {
    if (fs.existsSync(this.configPath)) {
      const explorer = cosmiconfig('agon');
      const result = explorer.searchSync();
      if (result && !result.isEmpty) {
        return { ...DEFAULT_CONFIG, ...result.config };
      }
    }
    return DEFAULT_CONFIG;
  }

  get<K extends keyof AgonConfig>(key: K): AgonConfig[K] {
    return this.config[key];
  }

  set<K extends keyof AgonConfig>(key: K, value: AgonConfig[K]): void {
    this.config[key] = value;
    this.save();
  }

  getAll(): AgonConfig {
    return { ...this.config };
  }

  private save(): void {
    fs.writeFileSync(this.configPath, JSON.stringify(this.config, null, 2));
  }

  reset(): void {
    this.config = { ...DEFAULT_CONFIG };
    this.save();
  }
}
```

---

## 6) Error Handling Strategy

### 6.1 Error Types

```typescript
// src/utils/errors.ts
export class AgonError extends Error {
  constructor(
    message: string,
    public code: string,
    public suggestion?: string
  ) {
    super(message);
    this.name = 'AgonError';
  }
}

export class APIError extends AgonError {
  constructor(message: string, public statusCode: number, suggestion?: string) {
    super(message, 'API_ERROR', suggestion);
    this.name = 'APIError';
  }
}

export class SessionNotFoundError extends AgonError {
  constructor(sessionId: string) {
    super(
      `Session ${sessionId} not found`,
      'SESSION_NOT_FOUND',
      `Run 'agon sessions' to see available sessions`
    );
    this.name = 'SessionNotFoundError';
  }
}

export class NetworkError extends AgonError {
  constructor() {
    super(
      'Cannot connect to Agon backend',
      'NETWORK_ERROR',
      'Make sure the backend is running on http://localhost:5000'
    );
    this.name = 'NetworkError';
  }
}
```

### 6.2 Error Handler

```typescript
// src/utils/error-handler.ts
import chalk from 'chalk';
import boxen from 'boxen';
import { AgonError } from './errors';
import { logger } from './logger';

export function handleError(error: unknown): void {
  logger.error('Command failed', { error });

  if (error instanceof AgonError) {
    console.error(
      boxen(
        `${chalk.red.bold('Error:')} ${error.message}\n\n` +
        (error.suggestion ? `${chalk.yellow('Suggestion:')} ${error.suggestion}` : ''),
        {
          padding: 1,
          margin: 1,
          borderStyle: 'round',
          borderColor: 'red',
        }
      )
    );
    process.exit(1);
  }

  // Unknown error - show generic message
  console.error(
    boxen(
      `${chalk.red.bold('Unexpected Error')}\n\n` +
      `${error instanceof Error ? error.message : String(error)}\n\n` +
      `${chalk.yellow('Check logs:')} ~/.agon/logs/agon.log`,
      {
        padding: 1,
        margin: 1,
        borderStyle: 'round',
        borderColor: 'red',
      }
    )
  );
  process.exit(1);
}
```

---

## 7) Testing Strategy

### 7.1 Command Tests

```typescript
// test/commands/start.test.ts
import { expect, test } from '@oclif/test';
import { AgonAPIClient } from '../../src/api/client';

describe('start command', () => {
  test
    .stub(AgonAPIClient.prototype, 'createSession', () => Promise.resolve({
      sessionId: 'test-session-id',
      phase: 'Intake',
      status: 'Active',
    }))
    .stdout()
    .command(['start', 'Test idea'])
    .it('creates a new session', ctx => {
      expect(ctx.stdout).to.contain('Session created');
      expect(ctx.stdout).to.contain('test-session-id');
    });

  test
    .command(['start'])
    .catch(err => expect(err.message).to.contain('Missing required argument: idea'))
    .it('requires an idea argument');

  test
    .stdout()
    .command(['start', 'Test idea', '--friction', '85'])
    .it('accepts friction flag', ctx => {
      expect(ctx.stdout).to.contain('friction: 85');
    });
});
```

### 7.2 API Client Tests

```typescript
// test/api/client.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { AgonAPIClient } from '../../src/api/client';
import axios from 'axios';

vi.mock('axios');

describe('AgonAPIClient', () => {
  let client: AgonAPIClient;

  beforeEach(() => {
    client = new AgonAPIClient('/tmp/test-config');
  });

  it('creates a session', async () => {
    const mockResponse = {
      data: {
        sessionId: 'test-id',
        phase: 'Intake',
        status: 'Active',
      },
    };
    
    vi.mocked(axios.create).mockReturnValue({
      post: vi.fn().mockResolvedValue(mockResponse),
    } as any);

    const session = await client.createSession('Test idea', 50);
    
    expect(session.sessionId).toBe('test-id');
    expect(session.phase).toBe('Intake');
  });
});
```

---

## 8) Build and Distribution

### 8.1 package.json Scripts

```json
{
  "scripts": {
    "build": "tsc",
    "dev": "ts-node --files ./bin/dev.js",
    "test": "vitest",
    "test:coverage": "vitest --coverage",
    "lint": "eslint . --ext .ts",
    "format": "prettier --write \"src/**/*.ts\"",
    "prepack": "npm run build",
    "postpack": "rm -f oclif.manifest.json",
    "version": "oclif readme && git add README.md"
  },
  "oclif": {
    "bin": "agon",
    "dirname": "agon",
    "commands": "./dist/commands",
    "plugins": [
      "@oclif/plugin-help",
      "@oclif/plugin-plugins"
    ],
    "topicSeparator": " "
  },
  "bin": {
    "agon": "./bin/run.js"
  }
}
```

### 8.2 Distribution Strategy

**Phase 1: npm Distribution**
```bash
npm publish
npm install -g @agon/cli
```

**Phase 2: Binary Distribution (oclif pack)**
```bash
# Build standalone binaries
oclif pack tarballs
oclif pack win

# Distribute via GitHub Releases
# Users can download and install without Node.js
```

---

## 9) Documentation Requirements

### 9.1 README.md

Must include:
- Quick start guide (install + first command)
- Example usage with screenshots (use term2svg for terminal recordings)
- Command reference (auto-generated by oclif)
- Configuration guide (.agonrc file format)
- Troubleshooting section

### 9.2 In-Command Help

Every command must have:
- Clear description
- Examples with realistic scenarios
- Flag/argument documentation
- Related commands section

---

## 10) Phase 1 MVP Scope

### Must Have
- [x] Project scaffolding (oclif + TypeScript)
- [ ] `agon start` - Create session + clarification loop
- [ ] `agon status` - Show current session status
- [ ] `agon show` - Display artifacts
- [ ] Session state caching (~/.agon/)
- [ ] Basic error handling
- [ ] Progress indicators (spinner)
- [ ] Markdown rendering in terminal

### Should Have
- [ ] `agon sessions` - List all sessions
- [ ] `agon resume` - Resume paused session
- [ ] Interactive clarification prompts
- [ ] Config file support (.agonrc)
- [ ] Comprehensive tests (>80% coverage)

### Could Have
- [ ] `agon challenge` - HITL claim challenging
- [ ] `agon constraint` - Mid-debate constraints
- [ ] `agon fork` - Pause-and-Replay
- [ ] Rich tables for Truth Map display
- [ ] Autocomplete (bash/zsh)

### Won't Have (Phase 2)
- Full-screen TUI mode (blessed)
- Real-time token streaming
- Export to PDF/HTML
- Multi-user collaboration
- Plugin system

---

## 11) Quality Gates

Before marking CLI Phase 1 complete:

- [ ] All MVP commands implemented and working
- [ ] Test coverage >80%
- [ ] Integration tests against real backend pass
- [ ] README with examples and screenshots
- [ ] Error handling tested (network failures, invalid input, etc.)
- [ ] Published to npm (scoped package: `@agon/cli`)
- [ ] Works on Mac/Linux/Windows
- [ ] CI/CD pipeline for automated releases

---

## 12) Reference CLIs

Study these for best practices:

- **GitHub CLI (`gh`):** Command structure, output formatting
  - https://github.com/cli/cli
- **Heroku CLI:** Plugin system, error handling
  - https://github.com/heroku/cli
- **Vercel CLI:** Interactive prompts, progress indicators
  - https://github.com/vercel/vercel
- **Stripe CLI:** Webhook streaming, API mocking
  - https://github.com/stripe/stripe-cli
