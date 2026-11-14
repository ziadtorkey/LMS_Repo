# M6 spike: Arabic message search (L21)

**Decision:** MySQL FULLTEXT with the `ngram` parser (`ngram_token_size` 2, the default) on `Messages.Body`, queried as a boolean-mode phrase
(`MATCH(Body) AGAINST('"term"' IN BOOLEAN MODE)`). No external search engine in v1.

**Why:** Arabic has no reliable word boundaries for the default parser, and a two-character token size finds both Arabic and English fragments. A phrase query
keeps the grams adjacent, so a search for "hello" does not match every message that contains "he".

**Checked** (`MessageSearchTests`, a MySQL 8.4 container): an English phrase, an Arabic word inside a sentence, the caller-only join, deleted messages, and the
200-hit window all behave as expected.

**Limits:**
- A query shorter than two characters is refused (400); the ngram index cannot serve it.
- Arabic morphology is not applied (no stemming, no diacritic folding): a search for a root does not find every inflected form. This is acceptable for v1; the
  fallback if QA3 or the owner finds it too weak on real samples is a normalised shadow column (diacritics and tatweel stripped, alef variants folded) with its
  own FULLTEXT index. Real Arabic samples are not part of this spike and were not downloaded.
- The index is created by raw SQL in the migration (the provider has no model support), so the model snapshot does not know it.
