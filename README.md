# MapLarge File Manager

Web-based file and directory browser built on the MapLarge .NET 8 starter project. One ASP.NET Core app hosts JSON APIs and a vanilla JavaScript SPA on the same origin.

## What it does

- Browse folders with pagination, sorting, breadcrumbs, and deep-linkable URLs
- Search file and folder names (no content indexing)
- Stream downloads with HTTP range support
- Create folders; delete, move, and copy files and folders
- Resumable chunked uploads (tus-inspired, not fully tus-compliant) up to a configurable 100 GB logical limit
- Full-page UI plus a reusable `<dialog>` file manager (`FileManager.openDialog`)

## Architecture

Deliberately simple:

| Layer | Responsibility |
| --- | --- |
| Controllers | Thin HTTP contracts |
| `FileService` / `SearchService` / `UploadService` | Filesystem and upload logic |
| `PathGuard` / `StorageRoot` | Path confinement and reparse-point rejection |
| `wwwroot/js/*` | ES-module SPA |

No ORM, no frontend framework, no third-party upload library, no Redis/SQL.

User data lives under `FileStorage:RootPath` (default `FileData`), **outside** `wwwroot`. Incomplete uploads use `{root}/.uploads/` and never appear in listings.

## Local setup (Visual Studio 2026 / .NET 8)

1. Open `TestProject.sln`.
2. Restore NuGet packages (automatic on build).
3. Press **F5** with the `TestProject` profile (`launchUrl` = `index.html`).
4. Or from a terminal:

```powershell
dotnet restore
dotnet build
dotnet run --project TestProject.csproj
```

5. Run tests:

```powershell
dotnet test
```

### Storage root

Default: `FileData` under the content root (created at startup).

Override:

```json
"FileStorage": {
  "RootPath": "D:\\Data\\MapLargeFiles"
}
```

Or environment variable: `FileStorage__RootPath=D:\Data\MapLargeFiles`

### Testing resumable uploads

1. Open the home page and choose **Upload**.
2. Watch progress; use **Pause** / **Resume** / **Cancel upload**.
3. Refresh mid-upload: use the recoverable **Resume** button and re-select the same file (name, size, and `lastModified` must match).
4. After an app restart, incomplete sessions under `FileData/.uploads` remain recoverable the same way.

## Configuration

| Section | Key | Default | Notes |
| --- | --- | --- | --- |
| FileStorage | RootPath | FileData | Absolute or content-root relative; not under wwwroot |
| FileStorage | MaxFileSizeGB | 100 | Logical ceiling |
| FileStorage | AllowOverwrite | false | Move/copy destination policy |
| FileStorage | ShowHiddenFiles | false | Hidden/system entries |
| FileStorage | StreamBufferKB | 64 | Copy streaming buffer |
| Browsing | DefaultPageSize | 100 | |
| Browsing | MaxPageSize | 500 | |
| Browsing | DefaultSort | Name | Name, Size, LastModified |
| Browsing | MaxFilesForSizeTotal | 2000 | Skip full directory size above this |
| Search | Enabled | true | |
| Search | MaxResults | 500 | |
| Search | TimeoutSeconds | 15 | |
| Search | MaxConcurrentSearches | 4 | |
| Search | IncludeSubdirectories | true | Default recursive flag |
| Uploads | Enabled | true | |
| Uploads | MaxFileSizeGB | 100 | |
| Uploads | ChunkSizeMB | 8 | Capped at 32 (IIS 64 MB request limit) |
| Uploads | StreamBufferKB | 64 | |
| Uploads | MaxConcurrentUploads | 4 | Session creation gate |
| Uploads | MaxRetries | 5 | Client guidance via `/api/config` |
| Uploads | RetryInitialDelaySeconds | 2 | |
| Uploads | ResumeEnabled | true | |
| Uploads | SessionExpirationHours | 72 | |
| Uploads | VerifyChunkChecksum | true | SHA-256 per chunk |
| Uploads | VerifyFinalChecksum | false | Optional full-file SHA-256 |
| Uploads | AllowOverwrite | false | |
| Features | Enable* | true | Server-enforced feature flags |
| Security | AllowAnonymousAccess | false | `true` only in Development by default |

Environment overrides use `__`, for example `Features__EnableDelete=false`.

`GET /api/config` returns only public client settings (no filesystem paths).

## API overview

### Config

- `GET /api/config`

### Files

- `GET /api/files?path=&page=&pageSize=&sort=&direction=`
- `GET /api/files/download?path=`
- `POST /api/files/folders` `{ "path", "name" }` → 201
- `POST /api/files/delete` `{ "path", "recursive" }` → 204
- `POST /api/files/move` `{ "sourcePath", "destinationDirectory", "destinationName?" }`
- `POST /api/files/copy` same body as move

Listing responses separate **page** counts/sizes from **directory** totals. When a directory has more files than `MaxFilesForSizeTotal`, `directorySizeComplete` is `false`.

### Search

- `GET /api/search?q=&path=&recursive=&maxResults=`

Matching is ordinal case-insensitive (Windows-oriented). Truncation sets `truncated` and `truncationReason` (`max_results` or `timeout`).

### Uploads (tus-inspired)

- `POST /api/uploads` → create session
- `GET /api/uploads/{id}` → authoritative offset/status
- `PUT /api/uploads/{id}/chunk?offset=` with body bytes and optional `X-Chunk-Checksum`
- `POST /api/uploads/{id}/complete`
- `DELETE /api/uploads/{id}`

Offset mismatches and concurrent writers return **409**. Checksum failures return **400** and do not advance the confirmed offset. Oversized payloads return **413**.

This protocol is **not** fully tus-compliant (custom JSON contracts and checksum header).

## Resumable upload design

1. **Create** allocates `{root}/.uploads/{id}/` with `data.bin`, `meta.json`.
2. **Chunks** must start at the server-confirmed offset; bytes stream to disk with a 64 KB buffer.
3. **Commit**: flush data, validate checksum, then atomically rewrite `meta.json` and advance `offset`.
4. **Complete**: verify length (and optional final hash), enforce overwrite policy, `File.Move` onto the destination.
5. **Recovery**: client stores id/name/size/`lastModified` in `localStorage`; after refresh the user reselects the file; client `GET`s offset before sending more data.
6. **Restart**: `GET` reconciles `offset = min(metadata, file length)` and truncates the data file.
7. **Locking**: exclusive `session.lock` file. **Single App Service instance only.**
8. **Cleanup**: background service removes expired/cancelled/stale-complete sessions.

File identity after browser restart is name + size + `lastModified`, not a cryptographic proof.

## Security

- All user paths are confined with segment validation, reserved-name checks, canonicalization, and separator-bounded root checks.
- Reparse points (symlinks/junctions) are rejected along resolved paths and skipped during search/copy.
- Staging (`.uploads`) is hidden and blocked from browse/download/delete/move/copy.
- Downloads use `application/octet-stream` + attachment disposition; uploads are never served from `wwwroot`.
- Production defaults `Security:AllowAnonymousAccess=false`. `/api/*` requires authenticated ASP.NET identity **or** Azure App Service Easy Auth header `X-MS-CLIENT-PRINCIPAL`.
- **TOCTOU limit:** if an attacker who can already write inside the storage root replaces a directory with a junction between check and use, managed code cannot fully close the race. Deploy so only the app identity can write the root and create reparse points.

## Azure App Service (Windows)

1. Publish the web project to a **Windows** App Service (.NET 8).
2. Set **instance count = 1** (upload lock files are local).
3. Use persistent storage, for example:
   - `FileStorage__RootPath=D:\home\site\FileData`, or
   - a mounted Azure Files share path.
4. Do **not** store uploads on non-persistent temp instance storage.
5. Application Settings examples:

```
FileStorage__RootPath=D:\home\site\FileData
Security__AllowAnonymousAccess=false
Uploads__ChunkSizeMB=8
```

6. Enable **App Service Authentication** (Microsoft Entra ID) with **Require authentication**, or front the app with a private network / gateway that authenticates users. Deploying to Azure alone does not secure the APIs.
7. `web.config` sets IIS `maxAllowedContentLength` to 64 MB for chunk requests. Keep `Uploads:ChunkSizeMB` ≤ 32.
8. Plan capacity must support your storage quota; not every App Service plan can hold 100 GB files.
9. Configuration changes require an app restart.

## Performance notes

- Ordinary browse does **not** recurse or compute nested folder sizes.
- Name-sorted paging materializes directory entry names; Size/LastModified sorts stat the directory before paging.
- Search is a bounded filesystem walk, not an index. It will not provide low-latency global search over millions of files.
- Uploads and downloads stream; the server never buffers an entire large file in memory.
- See `perf/benchmarks.ps1` for local measurement scripts. Report only measured numbers.

## Tradeoffs (intentionally not built)

- No rename API (use move with a new name if needed)
- No multi-instance upload locking / Redis / SQL session store
- No Azure Blob backend
- No document content search or Lucene
- No custom auth UI or per-user folders
- Not fully tus protocol compatible

## Project layout

```
Controllers/     API endpoints (+ retained TestController)
Services/        File, search, upload, cleanup
Security/        PathGuard, StorageRoot
Configuration/   Options + validators
Middleware/      Exceptions, access control
Models/          DTOs
wwwroot/         SPA (HTML/CSS/ES modules)
TestProject.Tests/
perf/benchmarks.ps1
```
