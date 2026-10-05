"""Inspect connected cycling-path components in the downloaded OSM tiles."""

from collections import Counter, defaultdict, deque
from math import cos, radians, sqrt
from pathlib import Path
import xml.etree.ElementTree as ET


ROOT = Path(__file__).parent
nodes = {}
ways = {}
for file in ROOT.glob("*.osm"):
    for element in ET.parse(file).getroot():
        if element.tag == "node":
            nodes[int(element.attrib["id"])] = (
                float(element.attrib["lat"]), float(element.attrib["lon"])
            )
        elif element.tag == "way":
            ways[int(element.attrib["id"])] = (
                [int(n.attrib["ref"]) for n in element.findall("nd")],
                {t.attrib["k"]: t.attrib["v"] for t in element.findall("tag")},
            )


def segment_length(a, b):
    lat1, lon1 = nodes[a]
    lat2, lon2 = nodes[b]
    dy = (lat2 - lat1) * 111_200
    dx = (lon2 - lon1) * 111_200 * cos(radians((lat1 + lat2) / 2))
    return sqrt(dx * dx + dy * dy)


graph = defaultdict(list)
included = []
for way_id, (refs, tags) in ways.items():
    highway = tags.get("highway")
    bicycle = tags.get("bicycle")
    allowed = highway == "cycleway" or (
        highway in ("path", "footway") and bicycle in ("yes", "designated", "official")
    )
    if not allowed or bicycle == "no":
        continue
    included.append((way_id, tags))
    for a, b in zip(refs, refs[1:]):
        if a in nodes and b in nodes:
            length = segment_length(a, b)
            graph[a].append((b, length, way_id))
            graph[b].append((a, length, way_id))

visited = set()
components = []
for origin in graph:
    if origin in visited:
        continue
    queue = deque([origin])
    visited.add(origin)
    members = []
    total = 0
    component_ways = set()
    while queue:
        a = queue.popleft()
        members.append(a)
        for b, length, way_id in graph[a]:
            component_ways.add(way_id)
            total += length / 2
            if b not in visited:
                visited.add(b)
                queue.append(b)
    lats = [nodes[n][0] for n in members]
    lons = [nodes[n][1] for n in members]
    names = Counter(ways[w][1].get("name", "unnamed") for w in component_ways)
    components.append((total, members, component_ways, (min(lats), min(lons), max(lats), max(lons)), names))

print(f"{len(nodes)} nodes, {len(ways)} ways, {len(included)} rideable path ways")
for distance, members, way_ids, bounds, names in sorted(components, reverse=True)[:12]:
    print(f"{distance / 1000:.2f} km, {len(members)} nodes, {len(way_ids)} ways, bbox={bounds}, names={names.most_common(3)}")

print("Longest highway=cycleway ways:")
cycleways = []
for way_id, (refs, tags) in ways.items():
    if tags.get("highway") != "cycleway":
        continue
    length = sum(segment_length(a, b) for a, b in zip(refs, refs[1:]) if a in nodes and b in nodes)
    cycleways.append((length, way_id, tags.get("name", "unnamed"), nodes.get(refs[0]), nodes.get(refs[-1])))
for length, way_id, name, start, end in sorted(cycleways, reverse=True)[:20]:
    print(f"{length / 1000:.2f} km way={way_id} {name}: {start} -> {end}")
