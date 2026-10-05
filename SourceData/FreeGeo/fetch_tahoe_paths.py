"""Download OSM cycling-path geometry for the Tahoe West Shore prototype.

Data: © OpenStreetMap contributors, ODbL 1.0.
The response is source data, not an authored ride route.
"""

import json
from pathlib import Path

import requests


QUERY = '''[out:json][timeout:90];
way["highway"~"^(cycleway|path|footway)$"]["bicycle"!="no"]
(39.075,-120.20,39.185,-120.125);
out geom;'''

OUTPUT = Path(__file__).with_name("tahoe_west_shore_paths.json")


def main():
    for endpoint in (
        "https://overpass.kumi.systems/api/interpreter",
        "https://overpass.nchc.org.tw/api/interpreter",
    ):
        try:
            response = requests.get(endpoint, params={"data": QUERY}, timeout=120)
            response.raise_for_status()
            data = response.json()
            if "elements" not in data:
                raise ValueError("response lacks OSM elements")
            OUTPUT.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
            print(f"{len(data['elements'])} ways -> {OUTPUT}")
            return
        except (requests.RequestException, ValueError) as error:
            print(f"{endpoint}: {error}")
    raise RuntimeError("No Overpass endpoint returned route data")


if __name__ == "__main__":
    main()
