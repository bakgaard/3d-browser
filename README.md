# Patreon 3D Browser

A self-hosted web app to search, browse, and categorise your 3D-print miniature library
(`E:\Patreon` and friends) and jump straight to any model's folder in Windows Explorer.

- **Search** objects by name, artist, release, and category
- **Browse** an object's renders in a fast thumbnail grid + full-screen lightbox
- **Copy path** — a button copies the object's Windows folder path to your clipboard
- **Categorise** objects (human, elf, orc, goblin, troll, beholder, …); free-form, created on the
  fly, always stored with a capitalised first letter
- **Persisted** in PostgreSQL
- **Scan** button picks up new folders/renders without touching what you've already tagged
- **Containerised** with Docker + Docker Compose

Only objects that have at least one render image are shown in the UI. Model-only entries
(`.stl`/`.lys`/`.lyt` with no picture) stay linked in the database but are hidden from search.

## Stack

| Part          | Tech                                             |
|---------------|--------------------------------------------------|
| Frontend      | Angular 22 (standalone + signals), nginx         |
| Backend       | .NET 10 ASP.NET Core Web API, EF Core + Npgsql    |
| Database      | PostgreSQL 17                                     |
| Thumbnails    | SixLabors.ImageSharp (managed, disk-cached)       |

---

## Quick start

### 1. Prerequisites
- Docker Desktop (Compose v2+). In Docker Desktop → **Settings → Resources → File sharing**,
  make sure the drives holding your library (e.g. `E:` and `D:`) are shared.
- For the Explorer "open folder" button: either the **.NET SDK** on the host, or just PowerShell
  (a zero-build fallback is provided).

### 2. Configure paths
```powershell
copy .env.example .env
```
Edit `.env` if your library isn't at `E:\Patreon`. (It already matches your machine.)
- `PATREON_PATH` / `EPIC_PATH` — mount sources, **forward slashes** (`E:/Patreon`).
- `HOST_ROOT` — Windows path used to open Explorer, **backslashes** (`E:\Patreon`).

### 3. Build & run
```powershell
docker compose up -d --build
```
Open **http://localhost:8081**.

### 4. First scan
Click **Scan library** in the UI (top bar). Progress shows live. This reads the folder tree and
builds the database. Existing tags are always preserved on later scans — a scan only adds/updates.

### 5. Jumping to files on disk
Each object's detail page shows its full Windows folder path and a **Copy path** button. Click it,
then paste (`Win+E` → paste in the address bar) to open the folder in Explorer.

---

## How objects are grouped ("smart auto-grouping")

Your folders aren't consistent, so the scanner infers one **object** (the thing you search for and
tag) per miniature like this, within each release:

- Every **non-auxiliary sub-folder** becomes one object (e.g. `Karrash The Forest Shadow`). All
  renders/models beneath it — including its own `Supported`, `Lychee`, `Chitubox` folders — roll up
  into that object.
- **Loose files** sitting directly in a release folder (and files inside auxiliary folders like
  `Supported`/`Renders`) are grouped by **filename prefix** — the text before the first underscore,
  else the whole name. So a flat release with `Flokir_Axebow.stl`, `Flokir_Base.stl`,
  `Flokir_Sample.jpg` becomes one **Flokir** object.

Auxiliary folder names treated as "roll up into the parent": `supported`, `presupported`,
`unsupported`, `lychee`, `chitubox`, `stl`, `parts`, `renders`, `images`, `photos`, `previews`, …

The scan is **idempotent**: objects are matched on a stable key derived from their path and renders
on their absolute path, so re-scans keep your category tags and never duplicate rows.

### Shortcuts (`.lnk`)
`Epic Miniatures.lnk` points to `D:\3D\Inactive Patreon\Epic Miniatures`. Compose mounts that folder
too (`EPIC_PATH`), and `backend/appsettings.json → Library:Shortcuts` maps the shortcut name to it.
To add more shortcut artists: add a mount in `docker-compose.yml` and an entry under
`Library:Shortcuts`.

---

## Categories & region boxes

- **Object categories** — free-form tags on the object. Add them on the object detail page; new
  ones are created automatically and reused via autocomplete. Search by category from the filter bar.
  Names are stored with a capitalised first letter (the rest is left exactly as you type it), and
  matching is case-insensitive so "orc" and "Orc" are the same tag.
- **Render region boxes** (phase 2) — the database schema (`RenderRegion`) is already in place to
  draw a box on a specific render and tag "what's in it". The drawing UI is a planned fast-follow.

---

## Adding new folders later

1. Drop new artist/release folders into your library as usual.
2. Click **Scan library** again. New objects and renders appear; existing tags are untouched.

---

## Architecture / ports

```
Browser ──► frontend (nginx :8081) ──/api──► backend (:8080) ──► PostgreSQL (:5432)
                                                  │
                                                  └── reads E:\Patreon (mounted /data/patreon, RO)
```

- `8081` — web UI (change with `FRONTEND_PORT`)
- `8080` — backend API (exposed for debugging; UI uses it via the nginx `/api` proxy)

## Continuous integration & releases

Two GitHub Actions workflows, each following least-privilege (`permissions: {}` by default,
scopes granted per job):

- **`.github/workflows/ci.yml`** — on every push/PR to `main`, builds the backend (.NET 10) and
  frontend (Angular 22) to catch breakage. Only needs `contents: read`.
- **`.github/workflows/release.yml`** — on pushing a `v*` tag: builds and pushes both container
  images to the GitHub Container Registry (`contents: read` + `packages: write`), then creates a
  GitHub Release with generated notes (`contents: write`). Uses the built-in `GITHUB_TOKEN` — no
  personal access token or extra secrets required.

Cut a release:

```bash
git tag v0.1.0
git push origin v0.1.0
```

This publishes:

- `ghcr.io/<owner>/3d-browser-backend:0.1.0` (and `:0.1`, `:latest`, `:sha-<sha>`)
- `ghcr.io/<owner>/3d-browser-frontend:0.1.0` (and `:0.1`, `:latest`, `:sha-<sha>`)

## Run from published images (no local build)

Once a release exists, run the prebuilt images instead of building locally. Set `IMAGE_OWNER`
(and optionally `IMAGE_TAG`, `POSTGRES_PASSWORD`) in `.env`, then:

```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
```

Both images ship a container `HEALTHCHECK` so `docker compose ps` reports real health.

## Running the backend without Docker (optional)
The backend targets **.NET 10**; your host currently has .NET 9. To run it outside Docker, install
the .NET 10 SDK, start a local Postgres, set `ConnectionStrings__Default`, then `dotnet run` in
`backend/`. Otherwise Docker handles the .NET 10 toolchain for you.

## Troubleshooting
- **Scan finds nothing / "volume not mounted"** — confirm the drive is shared in Docker Desktop and
  `PATREON_PATH` is correct. Check `docker compose logs backend`.
- **Thumbnails missing for some files** — non-image "renders" or corrupt files are skipped; the
  original still opens in the lightbox.
- **An object I expected is missing** — objects with no render image are hidden by design. Add a
  render image to its folder and re-scan, and it will appear.
- **Reset the database** — `docker compose down -v` removes the `pgdata` and `thumbs` volumes, then
  `docker compose up -d --build` and re-scan.
