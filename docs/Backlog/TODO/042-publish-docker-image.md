# 042 — Publish Docker Image to Public Registry

**Status:** TODO
**Sequence:** 42
**Dependencies:** 41 (project rename)

---

## Summary

Publish a Docker image for the application to a public container registry so that users can pull and run it without cloning the repository or installing .NET tooling. The image should be built and published automatically via CI/CD on every release.

## Background

Currently, running the application requires cloning the repository, having the .NET 10 SDK installed, and running `dotnet run` from the AppHost project. Publishing a Docker image dramatically lowers the barrier to entry — users can pull the image and run it with a `docker compose up`.

This work also requires deciding what "flavour" of image to publish and what companion `docker-compose` files to provide.

## Image Flavours and Docker Compose Files

The application depends on external services (document DB and vector store). Rather than bundling these into the image, the intent is to provide a set of **example `docker-compose` files** that cover the most common configurations. The application image itself is the same in all cases; only the compose file differs.

Suggested compose file variants (documentation/templates only — no app code change needed):

| Compose file | Document DB | Vector Store |
|---|---|---|
| `docker-compose.mongodb-qdrant.yml` | MongoDB | Qdrant |
| `docker-compose.postgres.yml` | Postgres (pgvector) | Postgres (pgvector) |
| `docker-compose.postgres-qdrant.yml` | Postgres | Qdrant |

Users who want a custom topology (e.g. managed cloud DB + local vector store) can write their own compose file and configure the application via environment variables.

## Requirements

### Docker Image

1. Create a `Dockerfile` for the API service and web frontend (or a single multi-service image if appropriate for the Aspire-hosted topology).
2. The image must be configurable entirely via environment variables (see issue 043 for the config resolution order).
3. The image should run as a non-root user.
4. Use multi-stage builds to keep the final image small.
5. Tag images as `latest` and with the release version (semver).

### Docker Compose Templates

6. Create `docker/` directory at the repo root with the compose file variants listed above.
7. Each compose file must include:
   - The application service pointing to the published image.
   - The required DB/vector store services with persistent volumes.
   - A commented `.env` section documenting all required environment variables.
8. A `docker/README.md` explains which compose file to use and how to configure it.

### CI/CD

9. Add a GitHub Actions workflow that:
   - Builds the Docker image on every push to `main` and on release tags.
   - Publishes to GitHub Container Registry (`ghcr.io`) and optionally Docker Hub.
   - Tags `latest` on `main` pushes; tags `vX.Y.Z` on release tags.
   - Runs `dotnet test` before building the image — do not publish if tests fail.

### Documentation

10. Update `docs/UserGuides/getting-started.md` with Docker-based setup instructions as the primary path.
11. Keep the Aspire developer workflow documented as the secondary (developer) path.

## Acceptance Criteria

- [ ] A `Dockerfile` exists and produces a working image that can be run without .NET installed.
- [ ] At least two `docker-compose` variant files exist in `docker/` and are documented.
- [ ] The GitHub Actions workflow publishes to `ghcr.io` on `main` and on release tags.
- [ ] `docker compose -f docker/docker-compose.mongodb-qdrant.yml up` starts a fully working application.
- [ ] The image runs as a non-root user.
- [ ] `docs/UserGuides/getting-started.md` reflects the Docker-first setup path.

## Notes

- .NET Aspire's publish tooling (`aspire publish`) or `dotnet publish` with container support may simplify the Dockerfile. Evaluate both.
- Aspire 9+ supports publishing to a container registry directly. Investigate whether this can replace a hand-written Dockerfile.
- The image name should match the project's chosen name from issue 041.
