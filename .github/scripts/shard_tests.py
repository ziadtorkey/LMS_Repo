#!/usr/bin/env python3
"""Splits a test project into shards by test class and prints the `dotnet test --filter` expression of one shard.

The integration suite runs in one xUnit collection against one pair of containers, so it is serial; sharding it over several CI jobs
(each with its own containers) is the safe way to parallelise. A class always stays whole in one shard, so tests that share a class
fixture or rely on each other inside a class keep working.

    shard_tests.py <test project dir> <shard index, 1-based> <shard count> [weights.json] [-- dotnet test args]

Classes are weighed by measured seconds from the weights file (class full name -> seconds); a class that is not in the file (a new test
class) gets the mean weight, so new tests are spread, never dropped. Assignment is greedy longest-first, deterministic for a given test
list, so every shard computes the same plan. With shard index 0 the whole plan is printed as a table instead (for humans and for the
coverage check).
"""
import json
import re
import subprocess
import sys


def list_classes(project_dir: str, extra: list[str]) -> dict[str, int]:
    out = subprocess.run(
        ["dotnet", "test", project_dir, "--list-tests", *extra],
        check=True, capture_output=True, text=True,
    ).stdout
    classes: dict[str, int] = {}
    in_list = False
    for raw in out.splitlines():
        line = raw.rstrip()
        if line.strip() == "The following Tests are available:":
            in_list = True
            continue
        if not in_list or not line.startswith("    "):
            continue
        name = line.strip().split("(", 1)[0]  # drop theory arguments
        if "." not in name:
            continue
        klass = name.rsplit(".", 1)[0]  # Namespace.Class
        classes[klass] = classes.get(klass, 0) + 1
    return classes


def plan(classes: dict[str, int], weights: dict[str, float], count: int) -> list[list[str]]:
    # Measured seconds where known; otherwise the number of tests of the class (one unit per test) scaled to the measured mean, so a new
    # class is weighed by its size and a missing weights file still balances by test count.
    known = [weights[c] / classes[c] for c in classes if c in weights and classes[c]]
    per_test = sum(known) / len(known) if known else 1.0

    def weight(c: str) -> float:
        # The endpoint matrices are thousands of cheap theory rows (about half the cost of an average test), so they are discounted until
        # real timings exist (trx_weights.py on the first CI run's artifacts).
        return weights.get(c, classes[c] * per_test * (0.4 if "Matrix" in c else 1.0))

    ordered = sorted(classes, key=lambda c: (-weight(c), c))
    shards: list[list[str]] = [[] for _ in range(count)]
    load = [0.0] * count
    for c in ordered:
        i = min(range(count), key=lambda k: (load[k], k))
        shards[i].append(c)
        load[i] += weight(c)
    return shards


def main() -> int:
    args = sys.argv[1:]
    extra: list[str] = []
    if "--" in args:
        cut = args.index("--")
        args, extra = args[:cut], args[cut + 1:]
    project_dir, index, count = args[0], int(args[1]), int(args[2])
    weights = json.load(open(args[3], encoding="utf-8")) if len(args) > 3 else {}

    classes = list_classes(project_dir, extra)
    if not classes:
        print("no tests found", file=sys.stderr)
        return 1
    shards = plan(classes, weights, count)
    if index == 0:
        for n, shard in enumerate(shards, 1):
            tests = sum(classes[c] for c in shard)
            print(f"shard {n}: {len(shard)} classes, {tests} tests")
        return 0
    mine = shards[index - 1]
    # "Namespace.Class." with the trailing dot: a class never matches another whose name only starts the same.
    print("|".join("FullyQualifiedName~" + re.escape(c).replace("\\.", ".") + "." for c in mine))
    print(f"shard {index}/{count}: {len(mine)} of {len(classes)} classes", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
