# M4b package corpus

Input of the package-hosting ADR (M4b Task 3, spike questions S2, S3, S6, S10) and of the package kernel and SCORM tests (Tasks 8a, 8b, 9b, 10). Two parts: the **crafted hostile corpus** (this file's first half, built by BE3) and the **real packages** (second half, BE4/QA3: referenced by hash and licence, not committed unless the licence allows).

## 1. Crafted corpus (committed)

123 archives under `tests/Lms.IntegrationTests/Packages/Corpus/files/` (about 7 MB), indexed by `manifest.json` (name, file, kind, expected result, intent, notes, size, SHA-256). Code: `tests/Lms.IntegrationTests/Packages/Corpus/` (`CorpusCases.*.cs` are the cases, `RawZip.cs` the writer, `RawZipView.cs` an independent reader).

Every archive is a **valid SCORM 1.2 (or H5P) package plus exactly one defect**, so a rejection can only be for the intended reason.

| Kind | Expected | Count |
|---|---|---|
| `reject` | one `PackageRejectReasons` value (`expect` in the index) | 84 |
| `accept` | `accepted`: a quirk that is **not** an attack (Zip64, data descriptor, CP437/UTF-8 names, HTML+ZIP polyglot, SVG, literal `%2e%2e` name, BOM manifest, ...) | 13 |
| `runtime` | `accepted` by validation; the behaviour is exercised in a browser or by the prelude injector (sandbox probes S2/S3, injection S10) | 26 |

Defect classes: path escapes (zip-slip, `\`, absolute, drive, UNC, trailing dot/space, device names, over-long/deep, local header name ≠ central name), bombs (ratio, total, **lying headers**: real bytes decide), entry count, Zip64 (valid, and a 6 GB claim), symlink, encryption, executables and server scripts (14 extensions, case-insensitive), nested archives (8 types), duplicates (exact, case fold, NFC/NFD, in a directory segment), name encodings (bidi, control, BOM, NUL, invalid UTF-8 with the flag, CP437), structure (not a zip, empty, truncated, no manifest), polyglots, SVG with script, XML (external file/http entity, billion laughs, bare DOCTYPE, huge document, `xsi:schemaLocation` / `xml:base` that must never be fetched, `xml:base` hiding a missing launch file), manifest hrefs that leave the package (`..`, `http:`, `file:`, `javascript:`, `\`, `//`), multi-SCO and 2004 sequencing, framesets and iframes, H5P (missing/invalid/deep/huge JSON, traversal in `mainLibrary`, a `.php` in a library), prelude-injection pages (UTF-8 BOM, windows-1252, UTF-16 LE/BE, no `<head>`, no `<html>`, doctype and comments first, upper case, `<head>` inside a script or a comment, conditional comments, multi-page SCO, own `window.API`, catching the parent-walk `SecurityError`, reading state at `LMSInitialize`) and sandbox probes (storage, XHR to the own host, fetch outside, `top.location`, own navigation, `<meta refresh>`, popup, form submit, forged `postMessage`, `eval`, blob worker, CDN script).

### Using it

- **Hardening tests (Task 8a/8b):** `CorpusCases.All.Where(c => c.Kind == CorpusKind.Reject)`, feed `CorpusCases.CommittedFile(c)` to `ZipHardening` / the validation job with the caps the case's `Notes` name, and assert the reject reason equals `c.Expect`. `Accept` cases must pass the reader. `Heavy` cases (the bombs) expand to 50–160 MB when read.
- **Injection and sandbox tests (Task 9b/10, E2E):** `CorpusKind.Runtime` cases; the `Intent` says what to assert in the browser.
- **From `Lms.UnitTests`:** link the folder (`<None Include="..\Lms.IntegrationTests\Packages\Corpus\files\**" Link="Corpus\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`) and read `manifest.json`; the case code stays in the integration project.
- **Regenerate** (only when a case changes): `LMS_REGEN_CORPUS=1 dotnet test tests/Lms.IntegrationTests --filter CorpusFiles_are_regenerated`, then commit the files and `manifest.json`.

`CorpusTests` guards it: each committed file still contains its defect (proved on the raw bytes by a reader that does not share code with the code under test), the light cases regenerate byte for byte, the index matches the files, and every attack class of the plan is present.

### Assumptions the cases make (caps of #427 §7.1; tests set the caps explicitly)

10 000 entries, 1 GB / test total cap named per case, per-entry ratio 100:1, depth 20, name 255, `h5p.json`/`content.json` 1 MB and depth 32, manifest document 1 MB. Each heavy case's `Notes` names the test cap under which only its own rule can fire.

### Open points found while building it (for Task 1 / the ADR)

1. **Reasons added by wave 3b (#500):** `invalid_archive` (not a zip, empty, truncated, no entries) and `bad_name` (too long or deep, device names, trailing dot/space, local-vs-central name mismatch). The cases map to them; `CorpusReasons` copies the catalog until #500 is on main.
2. **Accept cases that the ADR may flip:** the HTML+ZIP polyglot prefix, the literal `%2e%2e%2f` name. They are accepted here because the name or prefix is harmless once stored literally and never served; if the ADR prefers to reject, change `expect` and the case's kind.
3. **Not covered (follow-ups):** overlapping-entry zip quines, multi-disk archives, a real >4 GB Zip64 entry (only the 64-bit *claim* is tested), a zip comment of 65 535 bytes, and hardlinks (they do not exist in zip; tar is rejected as a nested archive).

## 2. Real packages (BE4 / QA3)

To be filled by the ADR (Task 3): 3 SCORM (1.2 single-SCO, 2004 4th edition, one authoring-tool export that uses a frameset or nested frames) and 2 H5P (a Course Presentation, an Interactive Video or Question Set), each with source URL, licence, SHA-256 and the result of the S2/S3/S10 runs.
