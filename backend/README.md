# Agon Backend - Development Setup

## Prerequisites

- .NET 9.0 SDK
- Docker Desktop
- Your LLM API keys in `../../.env` file

## Quick Start

### 1. Start Infrastructure (PostgreSQL + Redis)

```bash
docker-compose up -d
```

This will start:
- **PostgreSQL** on `localhost:5432`
  - Database: `agon`
  - User: `agon_user`
  - Password: `agon_dev_password`
- **Redis** on `localhost:6379`

### 2. Run Database Migrations

```bash
cd src/Agon.Api
dotnet ef database update
```

### 3. Load Environment Variables

The API will automatically load API keys from `../../.env`:
- `OPENAI_KEY`
- `CLAUDE_KEY`
- `GEMINI_KEY`
- `DEEPSEEK_KEY`

### 4. Run the API

```bash
dotnet run --project src/Agon.Api/Agon.Api.csproj
```

The API will be available at:
- HTTPS: `https://localhost:7001`
- HTTP: `http://localhost:5000`
- OpenAPI/Swagger: `https://localhost:7001/openapi/v1.json`

### 5. Run All Tests

```bash
dotnet test
```

---

## Stopping Infrastructure

```bash
docker-compose down
```

To remove volumes (clear data):

```bash
docker-compose down -v
```

---

## Optional: PgAdmin (Database GUI)

Start with the tools profile:

```bash
docker-compose --profile tools up -d
```

Access PgAdmin at `http://localhost:5050`:
- Email: `admin@agon.local`
- Password: `admin`

Connect to PostgreSQL:
- Host: `postgres` (or `localhost` from host machine)
- Port: `5432`
- Database: `agon`
- Username: `agon_user`
- Password: `agon_dev_password`

---

## Troubleshooting

### Port Already in Use

If ports 5432 or 6379 are already in use:

```bash
# Check what's using the port
lsof -i :5432
lsof -i :6379

# Kill the process or change ports in docker-compose.yml
```

### Database Connection Issues

Check if PostgreSQL is healthy:

```bash
docker-compose ps
docker-compose logs postgres
```

### Redis Connection Issues

Check if Redis is healthy:

```bash
docker-compose logs redis
redis-cli ping
```

---

## Development Workflow

1. Make code changes
2. Run tests: `dotnet test`
3. Run API: `dotnet run --project src/Agon.Api`
4. Test endpoints using:
   - Swagger UI at `/openapi/v1.json`
   - cURL
   - Postman/Insomnia
   - Your Next.js frontend

---

## Test Coverage

Current test suite:
- **Domain Tests**: 66 tests
- **Application Tests**: 56 tests
- **Infrastructure Tests**: 42 tests
- **API Tests**: 7 tests
- **Total**: 171 tests ✅
