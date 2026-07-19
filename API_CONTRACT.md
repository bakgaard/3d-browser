# Patreon 3D Browser — API Contract

Backend base URL (through nginx proxy in prod): `/api`
In dev the Angular app proxies `/api` → `http://localhost:8080`.

All IDs are integers. All list responses are JSON. Images are streamed bytes.

## Reference / lookups

### GET /api/artists
```json
[{ "id": 1, "name": "Artisan Guild", "releaseCount": 42, "objectCount": 610 }]
```

### GET /api/releases?artistId={id?}
```json
[{ "id": 5, "artistId": 1, "artistName": "Artisan Guild",
   "name": "2019.04 - Dwarfs", "code": "2019.04", "year": 2019, "month": 4,
   "objectCount": 3 }]
```

### GET /api/categories
```json
[{ "id": 2, "name": "orc", "objectCount": 14 }]
```

### POST /api/categories
Body: `{ "name": "beholder" }` → `201 { "id": 9, "name": "beholder", "objectCount": 0 }`
Idempotent by case-insensitive name (returns existing if present).

## Search

### GET /api/search
Query params (all optional): `q` (free text on object/artist/release name), `artistId`, `releaseId`, `categoryId`, `page` (default 1), `pageSize` (default 48).
```json
{
  "total": 1234, "page": 1, "pageSize": 48,
  "items": [{
    "id": 100, "name": "Karrash The Forest Shadow",
    "artistId": 6, "artistName": "White Werewolf Tavern",
    "releaseId": 20, "releaseName": "2020.09 - Werewolfs",
    "thumbnailRenderId": 501, "renderCount": 2,
    "categories": [{ "id": 3, "name": "werewolf" }]
  }]
}
```
`thumbnailRenderId` is null when the object has no renders (models only).

## Object detail

`name` is the display name = `customName ?? autoName`. `primaryRenderId` is the chosen thumbnail
render (null → falls back to first render by filename).

### GET /api/objects/{id}
```json
{
  "id": 100, "name": "Karrash The Forest Shadow",
  "customName": null, "autoName": "Karrash The Forest Shadow",
  "artistId": 6, "artistName": "White Werewolf Tavern",
  "releaseId": 20, "releaseName": "2020.09 - Werewolfs",
  "folderRelPath": "White Werewolf Tavern/2020.09 - Werewolfs/Karrash The Forest Shadow",
  "hostPath": "E:\\Patreon\\White Werewolf Tavern\\2020.09 - Werewolfs\\Karrash The Forest Shadow",
  "modelFileCount": 3,
  "primaryRenderId": 501,
  "categories": [{ "id": 3, "name": "Werewolf" }],
  "renders": [{ "id": 501, "fileName": "Karrash the Forest Shadow.jpg", "relPath": "..." }]
}
```

### PUT /api/objects/{id}/name
Body: `{ "customName": "Karrash (final paint)" }` — set a custom display title. Send `null` or empty
to clear it (revert to the scanned name). → `200` returns the updated ObjectDetail.

### PUT /api/objects/{id}/primary-render
Body: `{ "renderId": 502 }` — choose which render is the thumbnail. The render must belong to the
object. → `200` returns the updated ObjectDetail.

### POST /api/objects/{id}/merge
Body: `{ "sourceIds": [201, 202] }` — merge the given objects INTO `{id}` (the target/kept object).
All of their renders move to the target, categories are unioned, model counts summed. Sources are
hidden and stay merged across re-scans. → `200` returns the updated (target) ObjectDetail.

### POST /api/objects/{id}/categories
Body: `{ "categoryId": 3 }` OR `{ "name": "goblin" }` (creates category if needed).
→ `200` returns the object's updated category list `[{id,name}]`.

### DELETE /api/objects/{id}/categories/{categoryId}
→ `204`.

## Renders (images)

### GET /api/renders/{id}/thumbnail
Returns a JPEG/PNG thumbnail (long edge 400px), generated on demand and disk-cached. `Cache-Control: public, max-age=...`.

### GET /api/renders/{id}/file
Returns the full-resolution original image bytes.

## Scan

### POST /api/scan
Starts a scan in the background (no-op if already running). → `202 { "running": true }`.

### GET /api/scan/status
```json
{
  "running": true, "startedAt": "2026-07-18T10:00:00Z", "finishedAt": null,
  "phase": "White Werewolf Tavern",
  "artistsScanned": 3, "releasesFound": 40, "objectsFound": 610,
  "rendersFound": 1200, "objectsAdded": 12, "rendersAdded": 30,
  "warnings": ["Shortcut target not mounted: D:\\..."],
  "message": "Scanning..."
}
```

## Region annotations (schema present now; UI is phase 2)
Entity `RenderRegion`: `{ id, renderId, x, y, width, height (all normalized 0..1), categoryId, note }`.
Planned endpoints (not required for v1 UI): `GET/POST/DELETE /api/renders/{id}/regions`.
