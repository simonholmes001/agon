# Agon CLI

Command-line interface for Agon - AI-powered strategy debate platform.

## Installation

```bash
npm install -g @agon/cli
```

Or run directly with npx:

```bash
npx @agon/cli start "Your idea here"
```

## Quick Start

Start a new debate session:

```bash
agon start "Build a SaaS platform for freelance project management"
```

Check session status:

```bash
agon status
```

View generated artifacts:

```bash
agon show verdict
agon show plan
agon show prd
```

## Commands

### Core Commands

- `agon start <idea>` - Start a new debate session
- `agon status` - Show current session status
- `agon show <artifact>` - Display generated artifact

### Session Management

- `agon sessions` - List all sessions
- `agon resume <session-id>` - Resume a paused session

### Configuration

- `agon config` - Show current configuration
- `agon config set <key> <value>` - Set configuration value

## Configuration

Configuration is stored in `~/.agonrc` (YAML format):

```yaml
apiUrl: http://localhost:5000
defaultFriction: 50
researchEnabled: true
logLevel: info
```

## Development

### Setup

```bash
cd cli
npm install
npm run build
```

### Run Tests

```bash
npm test              # Run tests once
npm run test:watch   # Watch mode
npm run test:coverage # With coverage
```

### Development Mode

```bash
npm run dev          # Watch mode compilation
./bin/run.js start "test idea"  # Run locally
```

## Project Structure

```
cli/
├── bin/              # CLI entry point
├── src/
│   ├── commands/     # oclif command classes
│   ├── api/          # Backend API client
│   ├── ui/           # Terminal UI components (ink)
│   ├── state/        # Local state management
│   └── utils/        # Utilities
├── test/             # Test files
└── docs/             # Documentation
```

## Architecture

- **Framework**: oclif (used by Heroku CLI, GitHub CLI)
- **UI**: ink (React for terminals)
- **HTTP**: axios with retry logic
- **Testing**: vitest with >80% coverage requirement
- **State**: Local caching in `~/.agon/` directory

## Local State

The CLI caches data locally in `~/.agon/`:

```
~/.agon/
├── config.yaml              # User configuration
├── current-session          # Active session ID
├── sessions/
│   └── <session-id>.json    # Cached session state
├── artifacts/
│   └── <session-id>/
│       ├── verdict.md
│       ├── plan.md
│       └── ...
└── logs/
    └── agon.log             # Debug logs
```

## Contributing

See [CONTRIBUTING.md](../CONTRIBUTING.md) for development guidelines.

## License

MIT
