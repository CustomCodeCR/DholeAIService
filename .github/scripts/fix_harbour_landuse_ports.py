from pathlib import Path

path = Path('src/Dhole.AI.Api/Endpoints/AiLogisticsEndpoints.cs')
text = path.read_text(encoding='utf-8')


def replace_once(old: str, new: str, label: str) -> None:
    global text
    if old not in text:
        raise SystemExit(f'{label} not found')
    text = text.replace(old, new, 1)


replace_once(
    'nwr(around:{radiusMeters},{lat},{lon})[\\"harbour\\"];nwr(around:{radiusMeters},{lat},{lon})[\\"seamark:type\\"=\\"harbour\\"];',
    'nwr(around:{radiusMeters},{lat},{lon})[\\"harbour\\"];nwr(around:{radiusMeters},{lat},{lon})[\\"landuse\\"=\\"harbour\\"];nwr(around:{radiusMeters},{lat},{lon})[\\"seamark:type\\"=\\"harbour\\"];',
    'include landuse=harbour in Overpass radius search',
)

replace_once(
    '        var harbour = ReadString(tags, "harbour")?.ToLowerInvariant();\n        var seamarkType = ReadString(tags, "seamark:type")?.ToLowerInvariant();',
    '        var harbour = ReadString(tags, "harbour")?.ToLowerInvariant();\n        var landuse = ReadString(tags, "landuse")?.ToLowerInvariant();\n        var seamarkType = ReadString(tags, "seamark:type")?.ToLowerInvariant();',
    'read landuse harbour tag',
)

replace_once(
    '        var maritimeInfrastructure = industrial == "port"\n            || harbour == "yes"\n            || seamarkType == "harbour"',
    '        var maritimeInfrastructure = industrial == "port"\n            || harbour == "yes"\n            || landuse == "harbour"\n            || seamarkType == "harbour"',
    'accept landuse harbour as real maritime infrastructure',
)

path.write_text(text, encoding='utf-8')
print('landuse=harbour cargo-port discovery enabled.')
