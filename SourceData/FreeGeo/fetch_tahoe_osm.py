"""Fetch small OSM API map tiles for the Tahoe West Shore prototype.

Source: https://api.openstreetmap.org/api/0.6/map
License: © OpenStreetMap contributors, ODbL 1.0.
"""

from pathlib import Path
from time import sleep

import requests


ROOT = Path(__file__).parent
TILES = {
    "tahoe_city": (-120.17, 39.15, -120.13, 39.18),
    "west_shore_north": (-120.19, 39.12, -120.13, 39.15),
    "west_shore_middle": (-120.19, 39.09, -120.13, 39.12),
    "west_shore_south": (-120.19, 39.075, -120.13, 39.09),
}


def main():
    for name, bounds in TILES.items():
        target = ROOT / f"{name}.osm"
        if target.is_file() and target.stat().st_size > 0:
            print(f"cached {target.name}: {target.stat().st_size} bytes")
            continue
        response = requests.get(
            "https://api.openstreetmap.org/api/0.6/map",
            params={"bbox": ",".join(str(value) for value in bounds)},
            timeout=90,
            headers={"User-Agent": "WindTraceRide/0.1 (offline game prototype)"},
        )
        response.raise_for_status()
        target.write_bytes(response.content)
        print(f"saved {target.name}: {len(response.content)} bytes")
        sleep(2)


if __name__ == "__main__":
    main()
