"""Turn the public-domain USGS 3DEP West Shore elevation crop into mobile-sized OBJ tiles.

Run from any directory: python build_tahoe_terrain.py
The generated models share a local metre-based origin and can be imported by Unity.
"""

import json
from math import cos, radians
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).parent
SOURCE = ROOT / "tahoe_west_shore_3dep.tif"
META = ROOT / "tahoe_west_shore_3dep.meta.json"
TARGET = ROOT.parent.parent / "Assets" / "Resources" / "Art" / "Models" / "Geo" / "TahoeWestShore"
CELLS = 256
CHUNK_CELLS = 128
BASE_ELEVATION_METRES = 1880.0


def main():
    details = json.loads(META.read_text(encoding="utf-8-sig"))
    bounds = details["extent"]
    image = np.asarray(Image.open(SOURCE), dtype=np.float32)
    if image.shape != (1024, 1024) or not np.isfinite(image).all():
        raise ValueError("Unexpected 3DEP crop; check the source before exporting geometry")

    west, east = bounds["xmin"], bounds["xmax"]
    south, north = bounds["ymin"], bounds["ymax"]
    center_lat = (south + north) / 2
    center_lon = (west + east) / 2
    meters_per_lon = 111_320 * cos(radians(center_lat))
    meters_per_lat = 111_320

    # Sample the 1024x1024 DEM at 257 vertices.  Four 128-cell tiles keep each
    # imported Unity mesh below the 16-bit vertex-index limit.
    grid = np.empty((CELLS + 1, CELLS + 1), dtype=np.float32)
    for row in range(CELLS + 1):
        source_y = (1 - row / CELLS) * (image.shape[0] - 1)
        y0 = min(int(source_y), image.shape[0] - 2)
        ty = source_y - y0
        for col in range(CELLS + 1):
            source_x = col / CELLS * (image.shape[1] - 1)
            x0 = min(int(source_x), image.shape[1] - 2)
            tx = source_x - x0
            a = image[y0, x0] * (1 - tx) + image[y0, x0 + 1] * tx
            b = image[y0 + 1, x0] * (1 - tx) + image[y0 + 1, x0 + 1] * tx
            grid[row, col] = a * (1 - ty) + b * ty

    np.save(ROOT / "tahoe_west_shore_mesh_heights.npy", grid)

    TARGET.mkdir(parents=True, exist_ok=True)
    for tile_y in range(2):
        for tile_x in range(2):
            lines = ["# USGS 3DEP public-domain terrain; Tahoe West Shore\n", "o TahoeWestShoreTerrain\n"]
            for local_row in range(CHUNK_CELLS + 1):
                row = tile_y * CHUNK_CELLS + local_row
                lat = south + row / CELLS * (north - south)
                z = (lat - center_lat) * meters_per_lat
                for local_col in range(CHUNK_CELLS + 1):
                    col = tile_x * CHUNK_CELLS + local_col
                    lon = west + col / CELLS * (east - west)
                    # Unity's OBJ importer converts the right-handed file space by
                    # flipping X. Store -east here so the imported world uses +X east,
                    # matching the route JSON and water placement.
                    x = -(lon - center_lon) * meters_per_lon
                    height = float(grid[row, col]) - BASE_ELEVATION_METRES
                    lines.append(f"v {x:.3f} {height:.3f} {z:.3f}\n")
            stride = CHUNK_CELLS + 1
            for row in range(CHUNK_CELLS):
                for col in range(CHUNK_CELLS):
                    a = row * stride + col + 1
                    b = a + 1
                    c = a + stride
                    d = c + 1
                    lines.append(f"f {a} {c} {b}\nf {b} {c} {d}\n")
            output = TARGET / f"TahoeTerrain_{tile_x}_{tile_y}.obj"
            output.write_text("".join(lines), encoding="ascii")
            print(f"{output.name}: {output.stat().st_size:,} bytes")

    manifest = {
        "source": "USGS 3DEP Bare Earth DEM Dynamic Service",
        "sourceUrl": "https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer",
        "license": "Public Domain (USGS 3DEP)",
        "boundsWgs84": bounds,
        "originLatitude": center_lat,
        "originLongitude": center_lon,
        "baseElevationMeters": BASE_ELEVATION_METRES,
        "metersPerLongitudeDegree": meters_per_lon,
        "metersPerLatitudeDegree": meters_per_lat,
        "gridCells": CELLS,
    }
    (TARGET / "terrain-source.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"Elevation range: {grid.min():.1f}–{grid.max():.1f} m")


if __name__ == "__main__":
    main()
