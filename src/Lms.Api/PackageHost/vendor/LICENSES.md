# Vendored third-party code of the package host

Required by ADR 0005 section 9 (S8): every shipped asset is pinned to an exact version, with its source, licence and SHA-256 recorded here. Nothing under `vendor/` is edited; an update replaces the whole version folder and this file in one reviewed PR.

## h5p-standalone 3.8.2

| | |
|---|---|
| Purpose | The H5P player of the content host (M4b T11): renders an extracted H5P package from its own folder, no remote library fetching. |
| Version | **3.8.2** (the newest release on npm on 2026-11-14; registry `time.modified` 2026-03-24) |
| Source | npm registry tarball `https://registry.npmjs.org/h5p-standalone/-/h5p-standalone-3.8.2.tgz`, project `https://github.com/tunapanda/h5p-standalone` |
| Licence | **MIT**, Copyright (c) 2015 Tunapanda (`3.8.2/LICENSE`) |
| Tarball SHA-256 | `de7844ac07d5baddf5664c03738f25755914854b472f9b093251300ea1fec391` |
| npm integrity (sha512) | `sha512-r7eQtTzBk/dbaQZ+tA9XD5FeSmyMxlypRZXudiheCnC8zAT0/zNgUvO4MZtZaznwfYVo98o7do5uxLTUQrxxwg==` |
| npm shasum (sha1) | `ab7d8ff119fcbe0cf0b245b12cdb9bb1e6d4beb3` |
| Fetched | 2026-11-14 with `npm pack h5p-standalone@3.8.2 --ignore-scripts` into an empty scratch folder, extracted there, then copied: `dist/` and `LICENSE` only (no `package.json` scripts, tests or Cypress config). |
| Per-file hashes | `3.8.2/SHA256SUMS` (`sha256sum` of every vendored file; verify with `cd 3.8.2 && sha256sum -c SHA256SUMS`). |

### Licences of the shipped assets

| Asset | Licence |
|---|---|
| `dist/main.bundle.js`, `dist/frame.bundle.js`, `dist/styles/*`, `dist/images/*` | MIT (h5p-standalone, as declared in its `LICENSE` and `package.json`); the bundles also contain the H5P core runtime (its own licence is **not verified here**, see the note below) and `regenerator-runtime` (MIT, see `dist/main.bundle.js.LICENSE.txt`) |
| `dist/fonts/h5p-*`, `dist/fonts/*.woff*` (H5P icon fonts) | shipped with the H5P core; licence not stated in the package (see the note below) |
| `dist/fonts/inter/*` | Inter, SIL Open Font License 1.1 (`dist/fonts/inter/LICENSE.txt`) |
| `dist/fonts/open-sans/*` | Open Sans, SIL Open Font License 1.1 (`dist/fonts/open-sans/OFL.txt`) |

> **To confirm before T11 merges (QA3 / owner):** the H5P core code inside the bundles is covered by the upstream project's licence statement; the exact H5P core licence wording was not independently verified here. This file records what the package declares (`"license": "MIT"`, `LICENSE`), not a legal review.

### Checks done on the files (S3 evidence for the player itself)

- No `eval(` and no `new Function` in `main.bundle.js` or `frame.bundle.js`: the player does not need `'unsafe-eval'` (H5P content libraries are separate and still counted per package in S3).
- The URLs inside the bundles are namespace and licence identifiers (xAPI verbs, Creative Commons licence links, `h5p.org`), not fetched at run time by the player; the package host CSP (`connect-src 'self'`) would block any outside fetch anyway.
- Fonts and images are served from the vendored folder under the package host (`font-src 'self' data:`, `img-src 'self' data: blob:`).
