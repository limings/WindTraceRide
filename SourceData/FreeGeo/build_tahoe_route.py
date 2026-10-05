"""Extract a continuous West Shore cycling line from OSM and drape it on USGS 3DEP.

Outputs a reviewable GPX and a metre-based Unity route JSON. The OSM paths are
licensed ODbL 1.0; display © OpenStreetMap contributors in game credits.
"""

from collections import Counter, defaultdict
from heapq import heappop, heappush
import json
from math import cos, radians, sqrt
from pathlib import Path
import xml.etree.ElementTree as ET

import numpy as np


ROOT = Path(__file__).parent
TERRAIN = ROOT.parent.parent / "Assets" / "Resources" / "Art" / "Models" / "Geo" / "TahoeWestShore" / "terrain-source.json"
ROUTE_TARGET = ROOT.parent.parent / "Assets" / "Resources" / "GeoRoutes" / "TahoeWestShore.json"
START = (39.1399633, -120.1539148)
END = (39.0893838, -120.1627306)


def read_osm():
    nodes, ways = {}, {}
    for file in ROOT.glob("*.osm"):
        for element in ET.parse(file).getroot():
            if element.tag == "node":
                nodes[int(element.attrib["id"])] = (
                    float(element.attrib["lat"]), float(element.attrib["lon"])
                )
            elif element.tag == "way":
                ways[int(element.attrib["id"])] = (
                    [int(n.attrib["ref"]) for n in element.findall("nd")],
                    {tag.attrib["k"]: tag.attrib["v"] for tag in element.findall("tag")},
                )
    return nodes, ways


def metres(a, b):
    dy = (b[0] - a[0]) * 111_320
    dx = (b[1] - a[1]) * 111_320 * cos(radians((a[0] + b[0]) / 2))
    return sqrt(dx * dx + dy * dy)


def build_graph(nodes, ways):
    graph = defaultdict(list)
    for way_id, (refs, tags) in ways.items():
        kind, bicycle = tags.get("highway"), tags.get("bicycle")
        allowed = kind == "cycleway" or (
            kind in ("path", "footway") and bicycle in ("yes", "designated", "official")
        )
        if not allowed or bicycle == "no":
            continue
        for a, b in zip(refs, refs[1:]):
            if a not in nodes or b not in nodes:
                continue
            length = metres(nodes[a], nodes[b])
            graph[a].append((b, length, way_id))
            graph[b].append((a, length, way_id))
    return graph


def closest_node(graph, nodes, point):
    return min(graph, key=lambda node: metres(nodes[node], point))


def shortest_path(graph, start, end):
    queue = [(0.0, start)]
    best = {start: 0.0}
    predecessor = {}
    while queue:
        distance, node = heappop(queue)
        if distance != best[node]:
            continue
        if node == end:
            break
        for neighbor, length, way_id in graph[node]:
            candidate = distance + length
            if candidate < best.get(neighbor, float("inf")):
                best[neighbor] = candidate
                predecessor[neighbor] = (node, way_id)
                heappush(queue, (candidate, neighbor))
    if end not in best:
        raise ValueError("OSM paths are not connected between the chosen endpoints")
    result, way_ids = [end], []
    while result[-1] != start:
        parent, way_id = predecessor[result[-1]]
        result.append(parent)
        way_ids.append(way_id)
    result.reverse()
    way_ids.reverse()
    return result, way_ids, best[end]


def height_at(dem, bounds, lat, lon):
    px = (lon - bounds["xmin"]) / (bounds["xmax"] - bounds["xmin"]) * (dem.shape[1] - 1)
    # build_tahoe_terrain stores rows south-to-north so Unity Z increases northward.
    py = (lat - bounds["ymin"]) / (bounds["ymax"] - bounds["ymin"]) * (dem.shape[0] - 1)
    if not 0 <= px < dem.shape[1] - 1 or not 0 <= py < dem.shape[0] - 1:
        raise ValueError(f"Route point outside DEM: {lat}, {lon}")
    x0, y0 = int(px), int(py)
    tx, ty = px - x0, py - y0
    a = dem[y0, x0] * (1 - tx) + dem[y0, x0 + 1] * tx
    b = dem[y0 + 1, x0] * (1 - tx) + dem[y0 + 1, x0 + 1] * tx
    return float(a * (1 - ty) + b * ty)


def main():
    nodes, ways = read_osm()
    graph = build_graph(nodes, ways)
    start = closest_node(graph, nodes, START)
    end = closest_node(graph, nodes, END)
    if metres(nodes[start], START) > 10 or metres(nodes[end], END) > 10:
        raise ValueError("Could not find the intended OSM cycleway endpoints")
    path, path_ways, distance = shortest_path(graph, start, end)
    print(f"Path: {len(path)} points, {distance / 1000:.2f} km")
    print("Ways:", Counter(ways[way_id][1].get("name", "unnamed") for way_id in path_ways))
    print("Highway types:", Counter(ways[way_id][1].get("highway") for way_id in path_ways))

    metadata = json.loads(TERRAIN.read_text(encoding="utf-8"))
    bounds = metadata["boundsWgs84"]
    # Drape the ride line on the exact downsampled grid used by the Unity mesh.
    # Sampling the original 10 m raster here would let wheels float or sink
    # where the mobile terrain has been simplified.
    dem = np.load(ROOT / "tahoe_west_shore_mesh_heights.npy")
    center_lat, center_lon = metadata["originLatitude"], metadata["originLongitude"]
    route = []
    travelled = 0.0
    for index, node_id in enumerate(path):
        lat, lon = nodes[node_id]
        if index:
            travelled += metres(nodes[path[index - 1]], (lat, lon))
        elevation = height_at(dem, bounds, lat, lon)
        route.append({
            "latitude": lat,
            "longitude": lon,
            "x": round((lon - center_lon) * metadata["metersPerLongitudeDegree"], 3),
            "y": round(elevation - metadata["baseElevationMeters"], 3),
            "z": round((lat - center_lat) * metadata["metersPerLatitudeDegree"], 3),
            "distance": round(travelled, 3),
        })

    ROUTE_TARGET.parent.mkdir(parents=True, exist_ok=True)
    ROUTE_TARGET.write_text(json.dumps({
        "name": "Tahoe West Shore",
        "source": "OpenStreetMap / USGS 3DEP",
        "attribution": "© OpenStreetMap contributors (ODbL 1.0); USGS 3DEP (public domain)",
        "lengthMeters": round(travelled, 3),
        "points": route,
    }, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

    gpx = ['<?xml version="1.0" encoding="UTF-8"?>\n',
           '<gpx version="1.1" creator="WindTraceRide" xmlns="http://www.topografix.com/GPX/1/1">\n',
           '  <trk><name>Tahoe West Shore</name><trkseg>\n']
    for point in route:
        elevation = point["y"] + metadata["baseElevationMeters"]
        gpx.append(f'    <trkpt lat="{point["latitude"]:.7f}" lon="{point["longitude"]:.7f}"><ele>{elevation:.2f}</ele></trkpt>\n')
    gpx.extend(['  </trkseg></trk>\n', '</gpx>\n'])
    (ROOT / "tahoe_west_shore.gpx").write_text("".join(gpx), encoding="utf-8")
    gain = sum(max(0, route[i]["y"] - route[i - 1]["y"]) for i in range(1, len(route)))
    print(f"Elevation: {min(p['y'] for p in route):.1f}–{max(p['y'] for p in route):.1f} m relative, cumulative gain {gain:.1f} m")
    print(f"Saved {ROUTE_TARGET}")


if __name__ == "__main__":
    main()
