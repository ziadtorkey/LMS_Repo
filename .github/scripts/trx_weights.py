#!/usr/bin/env python3
"""Builds the shard weights file from one or more .trx results: class full name -> seconds of test time.

    trx_weights.py out.json run1.trx [run2.trx ...]

Run it on a full integration run (locally or the `integration-trx-*` artifacts of a CI run) and commit the result when the suite has
changed a lot; classes missing from the file simply get the mean weight, so the file may be stale without breaking anything.
"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict


def seconds(duration: str) -> float:
    h, m, s = duration.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def main() -> int:
    out, files = sys.argv[1], sys.argv[2:]
    totals: dict[str, float] = defaultdict(float)
    for path in files:
        root = ET.parse(path).getroot()
        ns = {"t": root.tag.split("}")[0].strip("{")}
        for result in root.iterfind(".//t:UnitTestResult", ns):
            name = result.get("testName", "")
            duration = result.get("duration")
            if not duration:
                continue
            klass = re.sub(r"\(.*$", "", name).rsplit(".", 1)[0]
            totals[klass] += seconds(duration)
    json.dump({k: round(v, 1) for k, v in sorted(totals.items())}, open(out, "w", encoding="utf-8"), indent=1)
    print(f"{len(totals)} classes, {sum(totals.values()) / 60:.1f} min of test time")
    return 0


if __name__ == "__main__":
    sys.exit(main())
