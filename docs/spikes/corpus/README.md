# Package-hosting corpus (ADR 0005, #470)

Status: **not yet collected**. Real packages are downloaded third-party files; they are fetched only with the product owner's approval, one file at a time, with name, source, size and licence recorded here. Where a licence forbids redistribution the file is referenced by SHA-256 and kept by QA instead of committed.

## Real packages (target)
| # | Kind | Needed for | File | Source | Licence | SHA-256 | Frameset / nested SCO | Needs `unsafe-eval` |
|---|---|---|---|---|---|---|---|---|
| 1 | SCORM 1.2 single-SCO | S2, S3, S10 | pending | pending | pending | pending | pending | pending |
| 2 | SCORM 2004 4th edition | S2, S3, S10 | pending | pending | pending | pending | pending | pending |
| 3 | SCORM authoring-tool export with its own player (at least one of the three uses a frameset or nested SCO frame) | S2, S3, S10 | pending | pending | pending | pending | pending | pending |
| 4 | H5P Course Presentation | S2, S3, S8 | pending | pending | pending | pending | n/a | pending |
| 5 | H5P Interactive Video or Question Set (loads JSON and media by XHR) | S2, S3, S8 | pending | pending | pending | pending | n/a | pending |

## Crafted archives (S6, built by BE3 under `tests/Lms.IntegrationTests/Packages/Corpus/`)
zip-slip (`..`, absolute, backslash), bombs (ratio and total), symlink/hardlink, polyglot (zip + HTML), duplicate after case/NFC fold, Zip64, nested archive, CP437 vs UTF-8 names, device names, encrypted entry, XXE/billion-laughs manifest, `xml:base`/`schemaLocation` to a local file and a URL, deep and huge `h5p.json`.
