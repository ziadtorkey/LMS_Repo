# Runbook: file storage settings

The `Storage` configuration section (server configuration or secret store, never the database). Secrets are never committed.

| Key | Default | Meaning |
|---|---|---|
| `Storage:Endpoint` | none | S3-compatible endpoint the API talks to (MinIO). |
| `Storage:PublicEndpoint` | `Endpoint` | Endpoint put into presigned URLs when browsers reach storage under another address. |
| `Storage:AccessKey`, `Storage:SecretKey` | none | Credentials. |
| `Storage:AllowedExtensions` | see `StorageOptions` | Comma separated allow-list. |
| `Storage:InMemoryReadLimitBytes` | 4 MiB | `IFileStorage.OpenReadAsync` reads objects up to this size into memory; larger ones are streamed into a temporary file. |
| `Storage:SpoolDirectory` | system temp directory | Where those temporary files go (#535). |

## Spool directory (deployment)

Background jobs that process uploads (package validation, imports) open the stored object with `OpenReadAsync`. An object larger than
`InMemoryReadLimitBytes` is copied from storage to a file named `lms-read-<guid>.tmp` in `Storage:SpoolDirectory`, opened
`DeleteOnClose`, so the file disappears when the job disposes the stream (also on failure or cancellation). Nothing is left behind unless the
process is killed mid-download.

- **Size it:** peak use is the number of concurrent large reads times the largest object. Package archives can be up to `Files.MaxUploadMb`
  (default 200 MB) and a server runs at most two package validations at once, so reserve at least 2 x `Files.MaxUploadMb` (400 MB at the
  default) of free space, plus whatever else shares the directory.
- **Point it at a dedicated writable volume** in containers (the image's default temp directory is often a small tmpfs or the container layer),
  for example `Storage__SpoolDirectory=/var/lib/lms/spool`. The process needs read and write access; no other user should.
- **Clean up after crashes:** if a node is killed during a download a `lms-read-*.tmp` file can stay behind. Delete files older than a day at
  startup or with a periodic host job (`find <dir> -name 'lms-read-*.tmp' -mmin +1440 -delete`).
- The directory is created on first use. The files hold uploaded content (possibly personal data of students), so do not put the directory on
  shared or world-readable storage.
