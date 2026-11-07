"""Prepare the bundled Steam identity map from reviewed catalog/provider data, offline."""

import argparse
import json
from pathlib import Path


def build_mappings(snapshot, shop_cache):
    mappings = {}
    for game in snapshot["Games"]:
        identity = game.get("ItadId")
        if not game.get("Reviewed") or not identity:
            continue
        products = shop_cache.get(identity) or []
        app_ids = {
            int(product[4:]) for product in products
            if isinstance(product, str) and product.startswith("app/")
            and product[4:].isascii() and product[4:].isdigit()
            and 0 < int(product[4:]) <= 4294967295
        }
        # Packages/bundles do not imply an app licence. Multiple apps need manual review.
        if len(app_ids) == 1:
            mappings[identity] = app_ids.pop()
    return dict(sorted(mappings.items()))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--cache", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    snapshot = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
    cache = json.loads(args.cache.read_text(encoding="utf-8-sig"))
    mappings = build_mappings(snapshot, cache)
    args.output.write_text(json.dumps(mappings, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared {len(mappings)} unambiguous Steam app mappings; review before shipping.")


if __name__ == "__main__":
    main()
