# MapLarge File Manager

Web-based file and directory browser built on the MapLarge .NET 8 starter project. One ASP.NET Core app hosts JSON APIs and a vanilla JavaScript SPA on the same origin.

## What it does

- Browse folders with pagination, sorting, breadcrumbs, and deep-linkable URLs (`path`, `page`, `q`, `sort`, `direction`)
- Search file and folder names (no content indexing), with an in-app Search help legend
- Stream downloads with HTTP range support
- Create folders; delete, move, and copy files and folders
- Resumable chunked uploads (tus-inspired, not fully tus-compliant) up to a configurable 100 GB logical limit
  - Pause / Resume, Simulate disconnect, Cancel, progress with current and average MB/s
  - Recovery after refresh or app restart via server session + `localStorage`
- Full-page UI plus a reusable `<dialog>` file manager (`FileManager.openDialog`) that honors the same URL query params
- Optional **Generate test data** button to seed a Star Wars demo tree for paging and search demos (idempotent; existing files are not overwritten)

## Architecture

Deliberately simple:

| Layer | Responsibility |
| --- | --- |
| Controllers | Thin HTTP contracts |
| `FileService` / `SearchService` / `UploadService` / `DemoDataService` | Filesystem, search, upload, and demo seed logic |
| `PathGuard` / `StorageRoot` | Path confinement and reparse-point rejection |
| `wwwroot/js/*` | ES-module SPA (`app`, `api`, `file-browser`, `upload`, `dialog`) |

No ORM, no frontend framework, no third-party upload library, no Redis/SQL.

User data lives under `FileStorage:RootPath` (default `FileData`), **outside** `wwwroot`. Incomplete uploads use `{root}/.uploads/` and never appear in listings. `FileData/` is gitignored and excluded from publish output.

## Local setup (Visual Studio / .NET 8)

1. Open `TestProject.sln`.
2. Restore NuGet packages (automatic on build).
3. Press **F5** with the `TestProject` profile (`launchUrl` = `index.html`).
   - Default URLs: `https://localhost:7146` and `http://localhost:5120` (see `Properties/launchSettings.json`).
4. Or from a terminal:

```powershell
dotnet restore
dotnet build
dotnet run --project TestProject.csproj
```

### Postman

JSON API requests live in `postman/MapLarge-File-Manager.postman_collection.json` (hardcoded `https://localhost:7146/`, no environment variables).

1. Start the app with the `TestProject` profile.
2. Import the collection into Postman.
3. Trust the ASP.NET development certificate, or turn off SSL verification for localhost.
4. Send any request (or run a folder). Session **07 Demo seed** writes the Star Wars tree under `FileData` (idempotent).

### Storage root

Default: `FileData` under the content root (created at startup). When the root has no user folders (ignoring `.uploads`), startup can seed `home/readme.md` if `FileStorage:SeedDefaultHomeWhenEmpty` is `true` (default).

Override:

```json
"FileStorage": {
  "RootPath": "D:\\Data\\MapLargeFiles"
}
```

Or environment variable: `FileStorage__RootPath=D:\Data\MapLargeFiles`

### Generate demonstration data

1. With the app running in Development, click **Generate test data** in the header (shown when `Features:EnableGenerateTestData` is true).
2. Or call `POST /api/demo/seed`.
3. The seed creates narrative folders (`00-Corporate` … `07-Media-Assets`), scale folders under `08-Scale-100x`, and two large paging directories. Existing paths are skipped.

### Testing resumable uploads

1. Open the home page and choose **Upload**.
2. Watch progress (percent plus current / average MB/s).
3. Use **Pause** / **Resume** — pause waits between chunks; an in-flight chunk may finish.
4. Use **Simulate disconnect** — aborts the in-flight request like a dropped connection; click **Resume** to re-query the server offset and continue.
5. Refresh mid-upload: use the recoverable **Resume** control and re-select the same file (name, size, and `lastModified` must match).
6. After an app restart, incomplete sessions under `FileData/.uploads` remain recoverable the same way.

## Configuration

Values below match shipped `appsettings.json` unless noted. `UploadsOptions` / other classes also have code defaults used when a key is omitted.

| Section | Key | Default | Notes |
| --- | --- | --- | --- |
| FileStorage | RootPath | FileData | Absolute or content-root relative; not under wwwroot |
| FileStorage | MaxFileSizeGB | 100 | Logical ceiling |
| FileStorage | AllowOverwrite | false | Move/copy destination policy |
| FileStorage | ShowHiddenFiles | false | Hidden/system entries |
| FileStorage | StreamBufferKB | 64 | Copy streaming buffer |
| FileStorage | SeedDefaultHomeWhenEmpty | true | Creates `home/readme.md` when the root has no user folders (tests disable this) |
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
| Uploads | ChunkSizeMB | **32** | Capped at 32 (IIS/Kestrel ~64 MB request limit). Code default is 8 if the key is omitted |
| Uploads | StreamBufferKB | 64 | |
| Uploads | MaxConcurrentUploads | 4 | Session creation gate |
| Uploads | MaxRetries | 5 | Client guidance via `/api/config` |
| Uploads | RetryInitialDelaySeconds | 2 | |
| Uploads | ResumeEnabled | true | |
| Uploads | SessionExpirationHours | 72 | |
| Uploads | VerifyChunkChecksum | true | SHA-256 per chunk |
| Uploads | VerifyFinalChecksum | false | Optional full-file SHA-256 |
| Uploads | AllowOverwrite | false | |
| Features | Enable* | true | Server-enforced feature flags (upload, download, search, create, delete, move, copy, dialog) |
| Features | EnableGenerateTestData | true | Header button + `POST /api/demo/seed` (defaults true even if omitted from JSON) |
| Security | AllowAnonymousAccess | false | `appsettings.Development.json` sets `true` for local F5 |

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

### Demo data

- `POST /api/demo/seed` → `{ createdDirectories, createdFiles, skippedExisting, elapsedMilliseconds }`  
  Requires `Features:EnableGenerateTestData`. Does not overwrite existing files or touch `.uploads`.

## Resumable upload design

1. **Create** allocates `{root}/.uploads/{id}/` with `data.bin`, `meta.json`.
2. **Chunks** must start at the server-confirmed offset; bytes stream to disk with a configurable buffer (default 64 KB).
3. **Commit**: flush data, validate checksum (when enabled), then atomically rewrite `meta.json` and advance `offset`.
4. **Complete**: verify length (and optional final hash), enforce overwrite policy, `File.Move` onto the destination.
5. **Recovery**: client stores id/name/size/`lastModified` in `localStorage`; after refresh the user reselects the file; client `GET`s offset before sending more data.
6. **Restart**: `GET` reconciles `offset = min(metadata, file length)` and truncates the data file.
7. **Locking**: exclusive `session.lock` file. **Single App Service instance only.**
8. **Cleanup**: background service removes expired/cancelled/stale-complete sessions.

**Pause** (UI) is cooperative between chunks. **Simulate disconnect** aborts the in-flight fetch; after Resume the client trusts the server offset.

File identity after browser restart is name + size + `lastModified`, not a cryptographic proof.

## Security

- All user paths are confined with segment validation, reserved-name checks, canonicalization, and separator-bounded root checks.
- Reparse points (symlinks/junctions) are rejected along resolved paths and skipped during search/copy.
- Staging (`.uploads`) is hidden and blocked from browse/download/delete/move/copy.
- Downloads use `application/octet-stream` + attachment disposition; uploads are never served from `wwwroot`.
- Production defaults `Security:AllowAnonymousAccess=false`. `/api/*` requires authenticated ASP.NET identity **or** Azure App Service Easy Auth header `X-MS-CLIENT-PRINCIPAL`.
- Feature flags are enforced on the server (hiding a UI button is not authorization).
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
Uploads__ChunkSizeMB=32
Features__EnableGenerateTestData=false
```

6. Enable **App Service Authentication** (Microsoft Entra ID) with **Require authentication**, or front the app with a private network / gateway that authenticates users. Deploying to Azure alone does not secure the APIs.
7. `web.config` sets IIS `maxAllowedContentLength` to 64 MB for chunk requests. Keep `Uploads:ChunkSizeMB` ≤ 32.
8. Plan capacity must support your storage quota; not every App Service plan can hold 100 GB files.
9. Configuration changes require an app restart.
10. Consider disabling **Generate test data** in production (`Features__EnableGenerateTestData=false`).

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
- No Swagger utility

## Project layout

```
Controllers/     Files, Search, Uploads, Config, Demo (+ retained TestController)
Services/        File, search, upload, cleanup, demo seed
Security/        PathGuard, StorageRoot
Configuration/   Options + validators
Middleware/      Exceptions, access control
Models/          DTOs
Exceptions/      FileManagerException
wwwroot/         SPA (HTML/CSS/ES modules)
perf/benchmarks.ps1
web.config       IIS request size limit for chunks
```
