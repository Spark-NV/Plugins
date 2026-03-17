#!/usr/bin/env python3
"""
Test script for Wikidata SPARQL request/response logic.
Mirrors the exact logic used in Jellyfin.Plugin.AutoCollections.WikidataApiClient.
Saves request and response to files next to this script for inspection.
"""

import json
import os
import sys
import urllib.parse
import urllib.request
from pathlib import Path

SPARQL_ENDPOINT = "https://query.wikidata.org/sparql"
USER_AGENT = "Jellyfin.Plugin.AutoCollections/1.0 (https://github.com/jellyfin/jellyfin)"
ACCEPT = "application/sparql-results+json"

SCRIPT_DIR = Path(__file__).resolve().parent


def escape_sparql_string(value: str) -> str:
    """Mirrors WikidataApiClient.EscapeSparqlString."""
    return value.replace("\\", "\\\\").replace('"', '\\"')


def extract_qid_from_uri(uri: str) -> str:
    """Mirrors WikidataApiClient.ExtractQidFromUri."""
    if not uri:
        return ""
    last_slash = uri.rfind("/")
    if last_slash >= 0 and last_slash < len(uri) - 1:
        return uri[last_slash + 1:]
    return uri


def build_query_list_p179(normalized_imdb_id: str) -> str:
    """List all P179 series for the movie - no member lookup.
    Uses p:P179/ps:P179 to get ALL values regardless of rank (wdt:P179 only returns preferred).
    """
    escaped = escape_sparql_string(normalized_imdb_id)
    return f'''
SELECT ?series ?seriesLabel WHERE {{
  ?movie wdt:P345 "{escaped}" .
  ?movie p:P179/ps:P179 ?series .
  ?series rdfs:label ?seriesLabel .
  FILTER(LANG(?seriesLabel) = "en")
}}
ORDER BY ?series'''


def build_debug_query(normalized_imdb_id: str) -> str:
    """Debug: show what properties each P179 series has, without filtering.
    Uses p:P179/ps:P179 to get ALL values regardless of rank.
    """
    escaped = escape_sparql_string(normalized_imdb_id)
    return f'''
SELECT DISTINCT ?series ?seriesLabel ?prop ?member ?imdbId WHERE {{
  ?movie wdt:P345 "{escaped}" .
  ?movie p:P179/ps:P179 ?series .
  ?series rdfs:label ?seriesLabel .
  FILTER(LANG(?seriesLabel) = "en")
  VALUES ?prop {{ wdt:P527 wdt:P1445 }}
  OPTIONAL {{
    ?series ?prop ?member .
    OPTIONAL {{ ?member wdt:P345 ?imdbId . }}
  }}
}}
ORDER BY ?series ?prop
LIMIT 100'''


def build_query_branch3_only(normalized_imdb_id: str, exclude_q642878: bool = False) -> str:
    """Test: Branch 3 only (siblings via P179) - no P527/P1445.
    Uses p:P179/ps:P179 to get ALL values regardless of rank.
    """
    escaped = escape_sparql_string(normalized_imdb_id)
    exclude = "  FILTER(?series != wd:Q642878)\n" if exclude_q642878 else ""
    return f'''
SELECT DISTINCT ?series ?seriesLabel ?imdbId WHERE {{
  ?movie wdt:P345 "{escaped}" .
  ?movie p:P179/ps:P179 ?series .
  ?series rdfs:label ?seriesLabel .
  FILTER(LANG(?seriesLabel) = "en"){exclude}
  ?member p:P179/ps:P179 ?series .
  ?member wdt:P345 ?imdbId .
  FILTER(STRSTARTS(?imdbId, "tt"))
}}
ORDER BY ?series ?imdbId
LIMIT 1000'''


def build_query(normalized_imdb_id: str) -> str:
    """Build the exact SPARQL query used in the plugin.
    P179 (part of the series) on movie -> find its collection.
    Uses p:P179/ps:P179 to get ALL values regardless of rank (wdt:P179 only returns preferred).
    Branch 1: P527 (has parts) on series -> members. Type filter: feature film only.
    Branch 2: P1445 (fictional universe) on series -> members. Type filter: feature film only.
    Branch 3: fallback - siblings that share P179. No type filter (trust P179 relationship).
    Q11424 = feature film - filters out TV, shorts, games on branches 1 & 2.
    """
    escaped = escape_sparql_string(normalized_imdb_id)
    return f'''
SELECT DISTINCT ?series ?seriesLabel ?imdbId WHERE {{
  ?movie wdt:P345 "{escaped}" .
  ?movie p:P179/ps:P179 ?series .
  ?series rdfs:label ?seriesLabel .
  FILTER(LANG(?seriesLabel) = "en")
  {{
    {{
      ?series wdt:P527 ?member .
      ?member wdt:P31/wdt:P279* wd:Q11424 .
    }}
    UNION
    {{
      ?series wdt:P1445 ?member .
      ?member wdt:P31/wdt:P279* wd:Q11424 .
    }}
    UNION
    {{
      # No type filter on sibling branch - trust P179 relationship
      ?member p:P179/ps:P179 ?series .
    }}
  }}
  ?member wdt:P345 ?imdbId .
  FILTER(STRSTARTS(?imdbId, "tt"))
}}
ORDER BY ?series ?imdbId
LIMIT 1000'''


def parse_sparql_json_response(json_str: str) -> list[dict]:
    """
    Mirrors WikidataApiClient.ParseSparqlJsonResponse.
    Groups bindings by series QID (each P179 entry is a distinct collection).
    Returns list of dicts with keys: series_qid, collection_name, imdb_ids.
    """
    try:
        data = json.loads(json_str)
    except json.JSONDecodeError as e:
        print(f"[ERROR] Failed to parse JSON: {e}")
        return []

    results = data.get("results")
    if not results:
        print("[WARN] Unexpected JSON structure: missing 'results'")
        return []

    bindings = results.get("bindings")
    if not bindings:
        print("[WARN] Unexpected JSON structure: missing 'results.bindings'")
        return []

    by_series: dict[str, tuple[str, set[str]]] = {}

    for binding in bindings:
        series_qid = None
        series_label = None
        imdb_id = None

        if "series" in binding:
            uri = binding["series"].get("value")
            if uri:
                series_qid = extract_qid_from_uri(uri)

        if "seriesLabel" in binding:
            series_label = binding["seriesLabel"].get("value")

        if "imdbId" in binding:
            val = binding["imdbId"].get("value")
            if val:
                trimmed = val.strip()
                if (
                    len(trimmed) >= 9
                    and trimmed.lower().startswith("tt")
                    and trimmed[2:].isdigit()
                ):
                    imdb_id = trimmed

        if not series_qid or not series_label or not imdb_id:
            continue

        if series_qid not in by_series:
            by_series[series_qid] = (series_label, set())
        by_series[series_qid][1].add(imdb_id)

    parsed = []
    for qid, (label, ids) in by_series.items():
        if ids:
            parsed.append({
                "series_qid": qid,
                "collection_name": label,
                "imdb_ids": sorted(ids),
            })
    return parsed


def main():
    print("Wikidata SPARQL Test Script")
    print("=" * 50)
    if len(sys.argv) > 1:
        imdb_input = sys.argv[1].strip()
        print(f"Using IMDb ID from argument: {imdb_input}")
    else:
        imdb_input = input("Enter IMDb ID to search (e.g. tt0322259 or 322259): ").strip()
    if not imdb_input:
        print("No ID provided. Exiting.")
        print("Usage: python test_wikidata_sparql.py [imdb_id]")
        sys.exit(1)

    # Normalize IMDb ID (mirror plugin logic)
    normalized_imdb_id = imdb_input.strip()
    if not normalized_imdb_id.lower().startswith("tt"):
        normalized_imdb_id = "tt" + normalized_imdb_id

    print(f"\nNormalized IMDb ID: {normalized_imdb_id}")

    # Check for --debug or --branch3 flags
    run_debug = "--debug" in sys.argv
    run_branch3_only = "--branch3" in sys.argv
    run_list_p179 = "--list-p179" in sys.argv

    # Build query and URL
    if run_list_p179:
        query = build_query_list_p179(normalized_imdb_id)
        print("\n[LIST P179] Listing all series the movie belongs to (no member lookup)")
    elif run_debug:
        query = build_debug_query(normalized_imdb_id)
        print("\n[DEBUG MODE] Running diagnostic query to show P527/P1445 per series")
    elif run_branch3_only:
        exclude_mcu = "--no-mcu" in sys.argv
        query = build_query_branch3_only(normalized_imdb_id, exclude_q642878=exclude_mcu)
        print("\n[BRANCH3 ONLY] Testing branch 3 (siblings via P179) - no P527/P1445")
        if exclude_mcu:
            print("[BRANCH3] Excluding Q642878 (MCU) to see other series")
    else:
        query = build_query(normalized_imdb_id)
    url = SPARQL_ENDPOINT + "?query=" + urllib.parse.quote(query) + "&format=json"

    # Save request
    request_file = SCRIPT_DIR / "wikidata_request.txt"
    with open(request_file, "w", encoding="utf-8") as f:
        f.write("=== REQUEST URL ===\n\n")
        f.write(url)
        f.write("\n\n=== SPARQL QUERY ===\n\n")
        f.write(query)
    print(f"\nRequest saved to: {request_file}")

    # Make request
    print("\nSending request to Wikidata...")
    req = urllib.request.Request(url)
    req.add_header("User-Agent", USER_AGENT)
    req.add_header("Accept", ACCEPT)

    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            response_body = resp.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        response_body = e.read().decode("utf-8") if e.fp else ""
        print(f"\n[ERROR] HTTP {e.code}: {e.reason}")
        print(f"Response body: {response_body[:500]}...")
    except Exception as e:
        print(f"\n[ERROR] Request failed: {e}")
        sys.exit(1)

    # Save raw response
    suffix = "_listp179" if run_list_p179 else "_debug" if run_debug else "_branch3" if run_branch3_only else ""
    response_file = SCRIPT_DIR / f"wikidata{suffix}_response.json"
    with open(response_file, "w", encoding="utf-8") as f:
        f.write(response_body)
    print(f"Response saved to: {response_file}")

    # Parse response (skip parsing for debug - just show raw)
    if run_list_p179:
        print("\n" + "=" * 50)
        print("P179 SERIES (movie has part of the series)")
        print("=" * 50)
        try:
            data = json.loads(response_body)
            bindings = data.get("results", {}).get("bindings", [])
            for b in bindings:
                uri = b.get("series", {}).get("value", "")
                qid = extract_qid_from_uri(uri) if uri else "?"
                label = b.get("seriesLabel", {}).get("value", "?")
                print(f"  {qid}: {label}")
            print(f"\nTotal: {len(bindings)} series")
        except json.JSONDecodeError as e:
            print(f"[ERROR] Failed to parse: {e}")
        print("\nDone.")
        sys.exit(0)
    if run_debug:
        print("\n" + "=" * 50)
        print("DEBUG QUERY RESULT (raw bindings)")
        print("=" * 50)
        try:
            data = json.loads(response_body)
            bindings = data.get("results", {}).get("bindings", [])
            for b in bindings:
                print(json.dumps(b, indent=2))
            print(f"\nTotal bindings: {len(bindings)}")
        except json.JSONDecodeError as e:
            print(f"[ERROR] Failed to parse: {e}")
        print("\nDone.")
        sys.exit(0)

    results = parse_sparql_json_response(response_body)
    if run_branch3_only:
        print(f"\n[BRANCH3 ONLY] Got {len(results)} series")
        for r in results:
            print(f"  - {r['collection_name']} (QID: {r['series_qid']}): {len(r['imdb_ids'])} members")
        print("\nDone.")
        sys.exit(0)


    # Save parsed result
    result_file = SCRIPT_DIR / "wikidata_parsed_result.json"
    if results:
        with open(result_file, "w", encoding="utf-8") as f:
            json.dump(results, f, indent=2)
        print(f"Parsed result saved to: {result_file}")

        print("\n" + "=" * 50)
        print(f"PARSED RESULT ({len(results)} series from P179)")
        print("=" * 50)
        for i, r in enumerate(results, 1):
            print(f"\n--- Series {i}: {r['collection_name']} (QID: {r['series_qid']}) ---")
            print(f"Member count: {len(r['imdb_ids'])}")
            for imdb_id in r["imdb_ids"][:20]:  # Show first 20
                print(f"  - {imdb_id}")
            if len(r["imdb_ids"]) > 20:
                print(f"  ... and {len(r['imdb_ids']) - 20} more")
            if len(r["imdb_ids"]) >= 1000:
                print("[WARN] Hit 1000-item limit; some members may be omitted.")
    else:
        print("\nNo series found or parsing failed.")
        if os.path.exists(result_file):
            os.remove(result_file)

    print("\nDone.")


if __name__ == "__main__":
    main()
