---
applyTo: '**'
---
# Agon Development Backlog

**Last Updated:** March 7, 2026  
**Current Phase:** Phase 1 - CLI MVP

---

## 🎯 Current Sprint (CLI Foundation)

### In Progress
- [ ] None

### To Do
- [ ] Create CLI project structure with oclif
- [ ] Implement `agon start` command (session creation + clarification)
- [ ] Implement `agon status` command (session state display)
- [ ] Implement `agon show` command (artifact display)
- [ ] Set up local state management (~/.agon/)
- [ ] Implement API client wrapper (axios + error handling)
- [ ] Add progress indicators (ora spinners)
- [ ] Add Markdown rendering (marked-terminal)
- [ ] Write command tests (oclif test utilities)
- [ ] Write API client tests (vitest + mocks)

### Done ✅
- [x] Backend test endpoint fixed (Truth Map CoreIdea seeding)
- [x] LLM model names corrected and verified (gpt-5.2, claude-opus-4-6, gemini-3-flash-preview)
- [x] MAF integration verified for all providers
- [x] Integration test infrastructure created
- [x] EF Core migrations applied (sessions, truth_maps tables)
- [x] Architecture documentation updated for CLI-first approach
- [x] CLI implementation guide created
- [x] Backlog file created

---

## 📋 Feature Backlog

### Phase 1: CLI MVP (Weeks 1-2)

**Goal:** Basic command-line interface for core debate workflow

#### Core Commands
- [ ] **`agon start <idea>`** - Create session + clarification loop
  - Accept `--friction` flag (0-100, default 50)
  - Accept `--research / --no-research` flag (default true)
  - Accept `--interactive / --no-interactive` flag (default true)
  - Show spinner during session creation
  - Display clarification questions interactively
  - Poll backend for round completion
  - Show success message with session ID

- [ ] **`agon status`** - Show current session status
  - Display session ID, phase, status
  - Show convergence score (if in debate)
  - Show current round number
  - Show tokens used / budget
  - Display contested claims count (if any)
  - Show estimated time remaining

- [ ] **`agon show <artifact>`** - Display artifact in terminal
  - Render Markdown with marked-terminal
  - Support artifact types: verdict, plan, prd, risks, assumptions, architecture, copilot
  - Use pager for long artifacts (less/more)
  - Cache artifacts locally after first fetch
  - Show warning if artifact not yet generated

#### Session Management
- [ ] **`agon sessions`** - List all sessions
  - Display table with: ID (short), Created, Status, Phase, Convergence
  - Sort by creation date (newest first)
  - Highlight current session
  - Support `--all` flag to show completed sessions

- [ ] **`agon resume <session-id>`** - Resume paused session
  - Validate session exists
  - Set as current session
  - Continue from last phase
  - Show status after resuming

#### Configuration
- [ ] **`agon config`** - Show current configuration
  - Display all config values in table
  - Show config file location (~/.agonrc)
  - Show default values vs overridden values

- [ ] **`agon config set <key> <value>`** - Set configuration value
  - Support keys: apiUrl, defaultFriction, researchEnabled, logLevel
  - Validate value types and ranges
  - Save to ~/.agonrc (YAML format)
  - Show confirmation message

#### Infrastructure
- [ ] **Local State Management**
  - Create ~/.agon/ directory structure on first run
  - Implement SessionManager class
  - Implement ConfigManager class (cosmiconfig)
  - Implement CacheManager class
  - Store current session ID in ~/.agon/current-session
  - Cache session data in ~/.agon/sessions/<id>.json
  - Cache artifacts in ~/.agon/artifacts/<session-id>/

- [ ] **API Client**
  - Implement AgonAPIClient class (axios wrapper)
  - Add request/response interceptors for logging
  - Add retry logic for transient failures (exponential backoff)
  - Add timeout handling (30s default)
  - Add error mapping (HTTP status → AgonError)
  - Support environment variable for API URL (AGON_API_URL)

- [ ] **Error Handling**
  - Define error types (AgonError, APIError, NetworkError, etc.)
  - Implement handleError utility (boxen + chalk)
  - Add friendly error messages with suggestions
  - Log full errors to ~/.agon/logs/agon.log
  - Never show raw stack traces to users

- [ ] **Testing**
  - Set up vitest for unit tests
  - Set up @oclif/test for command tests
  - Mock API client in all command tests
  - Write tests for SessionManager
  - Write tests for ConfigManager
  - Write tests for error handling
  - Achieve >80% test coverage
  - Add integration tests against real backend (optional, CI only)

- [ ] **Documentation**
  - Write comprehensive README.md
  - Add command examples with term2svg screenshots
  - Document .agonrc configuration format
  - Add troubleshooting section
  - Generate man pages (oclif readme)
  - Add CONTRIBUTING.md for CLI development

### Phase 2: Advanced CLI Features (Weeks 3-4)

**Goal:** HITL interactions and session management

#### HITL Commands
- [ ] **`agon challenge <claim-id>`** - Challenge a specific claim
  - Accept `--reason` flag (optional, prompts if missing)
  - Show claim text before challenging
  - Confirm action (destructive - triggers reevaluation)
  - Show spinner during reevaluation
  - Display affected entities count
  - Show updated convergence score

- [ ] **`agon constraint <text>`** - Add/modify constraint mid-debate
  - Validate constraint text (not empty, <500 chars)
  - Show impact set (entities that will be reevaluated)
  - Confirm action (can be expensive)
  - Show progress during reevaluation
  - Display summary of changes

- [ ] **`agon deepdive <entity-id>`** - Force targeted deep dive
  - Display entity text and current analysis
  - Show which agent will handle the deep dive
  - Show spinner during targeted round
  - Display updated entity after deep dive

#### Advanced Session Management
- [ ] **`agon fork <session-id>`** - Fork session from snapshot
  - List available snapshots (by round)
  - Accept `--from-round <n>` flag
  - Accept `--constraint <text>` flag (initial patch)
  - Create forked session with new ID
  - Set forked session as current
  - Show comparison summary

- [ ] **`agon export`** - Export artifacts to files
  - Support `--format` flag: markdown (default), pdf, html
  - Support `--output` flag: directory path
  - Export all artifacts for current session
  - Create zip archive if multiple files
  - Show file locations after export

#### UI Enhancements
- [ ] **Rich Tables** (cli-table3)
  - Use for sessions list
  - Use for Truth Map entity display
  - Use for configuration display
  - Add colors and styling

- [ ] **Interactive Prompts** (inquirer)
  - Clarification Q&A with numbered questions
  - Confirmation prompts for destructive actions
  - Multi-select for artifact export
  - Checkbox for session cleanup

- [ ] **Progress Tracking**
  - Show round progress (1/3, 2/3, 3/3)
  - Show agent completion status (✓ GPT, ✓ Gemini, ⏳ Claude)
  - Show convergence progress bar
  - Show token budget usage percentage

### Phase 3: Polish & Distribution (Week 5)

**Goal:** Production-ready CLI tool

#### Polish
- [ ] **Interactive TUI Mode** (blessed - optional)
  - Full-screen terminal interface
  - Real-time updates (no polling)
  - Split panes (debate thread + Truth Map)
  - Keyboard shortcuts (j/k navigation, q to quit)
  - Mouse support (optional)

- [ ] **Autocomplete** (oclif autocomplete)
  - Bash completion script
  - Zsh completion script
  - Fish completion script
  - Install via `agon autocomplete`

- [ ] **Colors and Styling**
  - Use chalk consistently throughout
  - Define color palette (success=green, error=red, warning=yellow, info=blue)
  - Add boxen for important messages
  - Use terminal-link for clickable URLs

- [ ] **Performance Optimization**
  - Cache API responses aggressively
  - Lazy-load artifacts (don't fetch until needed)
  - Parallel API calls where possible
  - Debounce polling during long operations

#### Distribution
- [ ] **npm Package**
  - Create scoped package (@agon/cli)
  - Set up npm publishing workflow
  - Add package.json keywords for discoverability
  - Add LICENSE file (MIT)
  - Add .npmignore

- [ ] **Binary Releases** (oclif pack)
  - Build standalone binaries (Mac, Linux, Windows)
  - Create GitHub Releases workflow
  - Add installation instructions for binary
  - Sign Mac binary (notarization)
  - Create Homebrew formula

- [ ] **CI/CD**
  - GitHub Actions workflow for tests
  - Automated npm publish on version tag
  - Automated binary builds on release
  - Code coverage reporting (Codecov)
  - Dependabot for dependency updates

---

## 🔄 Backend Backlog

### High Priority
- [ ] **SSE Endpoint for CLI** - Server-Sent Events for streaming
  - Implement `/sessions/{id}/events` SSE endpoint
  - Stream phase transitions, round completions, artifact ready events
  - Fall back to polling if SSE not supported
  - Add heartbeat to detect disconnections

- [ ] **Session Polling Optimization** - Reduce polling load
  - Add `Last-Modified` header to GET `/sessions/{id}`
  - Support `If-Modified-Since` header (304 Not Modified)
  - Add ETag support for cache validation
  - Document polling best practices in API docs

### Medium Priority
- [ ] **API Documentation** - OpenAPI/Swagger spec
  - Generate OpenAPI spec from controllers
  - Add Swagger UI endpoint (/api/docs)
  - Document all request/response schemas
  - Add example requests/responses
  - Document error codes and messages

- [ ] **Rate Limiting** - Protect API from abuse
  - Implement rate limiting middleware (AspNetCoreRateLimit)
  - Set limits: 100 requests/minute per IP
  - Return 429 Too Many Requests with Retry-After header
  - Add rate limit headers to all responses

- [ ] **API Versioning** - Prepare for breaking changes
  - Add version prefix to routes (/v1/sessions)
  - Support multiple versions simultaneously
  - Add deprecation warnings in responses
  - Document versioning strategy

### Low Priority
- [ ] **Metrics and Monitoring** - Production observability
  - Add Prometheus metrics endpoint (/metrics)
  - Track session creation rate
  - Track token usage by provider
  - Track convergence time distribution
  - Add health check endpoint (/health)

- [ ] **Authentication** - Multi-user support (Phase 2)
  - Implement JWT authentication
  - Add user registration/login endpoints
  - Associate sessions with users
  - Add rate limiting per user

---

## 🚫 Out of Scope (Phase 2+)

These features are explicitly **not** part of Phase 1:

### CLI Features
- Real-time token streaming (use progress indicators instead)
- Full-screen TUI mode (blessed interface)
- Export to PDF/HTML (Markdown only in Phase 1)
- Plugin system (oclif plugins)
- Multi-user collaboration
- Session sharing/publishing
- Voice input/output
- Mobile app integration

### Backend Features
- Multi-user workspaces
- Team collaboration features
- File attachment support
- RAG over user documents
- Continuous/subscription mode
- Custom agent configuration by end users
- Simulation mode
- Web UI (Phase 2)
- Map View visualization (Phase 2)
- Session Timeline Scrubber (Phase 2)

---

## 📈 Success Metrics (Phase 1)

### Technical Metrics
- [ ] All CLI commands implemented and working
- [ ] Test coverage >80%
- [ ] All integration tests passing
- [ ] Zero critical/high security vulnerabilities
- [ ] CLI startup time <500ms
- [ ] Command execution time <100ms (excluding API calls)

### User Experience Metrics
- [ ] README with usage examples
- [ ] Error messages tested (network failures, invalid input)
- [ ] Works on Mac/Linux/Windows
- [ ] Published to npm (installable via `npm install -g @agon/cli`)
- [ ] At least 3 example sessions documented

### Quality Metrics
- [ ] Code review completed
- [ ] No eslint errors or warnings
- [ ] All TypeScript strict mode enabled
- [ ] No console.log (use logger instead)
- [ ] No hardcoded values (use config)

---

## 🐛 Known Issues

### CLI
- None yet (project just started)

### Backend
- ⚠️ **NuGet package version warnings** - Infrastructure project references Anthropic.SDK 0.2.3 but 1.0.0 is resolved. Update to >=1.0.0 after testing.
- ⚠️ **Test coverage below target** - Infrastructure (74.6%) and API (49.4%) below 80% target. Need more integration tests.

---

## 💡 Ideas for Future Consideration

- GitHub Copilot CLI-style experience (`gh copilot explain` for "why this decision?")
- Debate replay mode (step through rounds like `git log`)
- Export to GitHub Issues/Project (convert risks/tasks to issues)
- Slack/Discord bot integration (run debates from chat)
- VS Code extension (run debates from editor, insert artifacts into code)
- JetBrains plugin (IntelliJ, PyCharm, etc.)
- Emacs/Vim plugins (for the true terminal purists)
- AI-powered search over debate history ("find sessions about auth")
- Collaborative sessions (multiple users, real-time sync)
- Session templates (pre-configured debates for common scenarios)

---

## 📝 Notes

- This backlog is a living document. Update it as tasks are completed or priorities change.
- Mark tasks as ✅ when complete, including the date.
- Move tasks between sections as needed (e.g., Medium → High priority).
- Add new tasks to the bottom of the appropriate section.
- Reference GitHub issue numbers where applicable (e.g., `- [ ] Fix bug #123`).
- For large features, break them down into smaller subtasks.
