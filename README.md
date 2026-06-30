# AiKnowledgePlatform

AiKnowledgePlatform is a simple .NET API for uploading and managing knowledge documents.

## Tech Stack

- .NET 10
- ASP.NET Core Web API
- PostgreSQL
- Redis
- Qdrant
- Docker Compose

## Current Features

- `GET /health`
- `POST /documents`
- PDF upload validation
- Local file storage under `storage/documents/{documentId}/`

## Run Locally

Start infrastructure:

```powershell
docker compose up -d
```

Run the API:

```powershell
dotnet run --project src/AiKnowledgePlatform.Api
```

Default local URL:

```text
http://localhost:5034
```

## Test Upload With Curl

```bash
curl -X POST http://localhost:5034/documents \
  -F "file=@sample.pdf;type=application/pdf"
```

## Project Structure

```text
src/
  AiKnowledgePlatform.Api/
    Extensions/
    Features/
      Documents/
      Health/
    Storage/
tests/
  AiKnowledgePlatform.Tests/
docker-compose.yml
AiKnowledgePlatform.slnx
```

## Completed Sprints

- Sprint 0: Foundation
  - .NET 10 solution
  - ASP.NET Core Web API
  - Health endpoint
  - Docker Compose for PostgreSQL, Redis, and Qdrant

- Sprint 1: Document Upload
  - PDF upload endpoint
  - PDF validation
  - Local document storage
  - Metadata file creation
  - Integration tests

## Next Sprint

- Document metadata
- `GET /documents/{id}`
