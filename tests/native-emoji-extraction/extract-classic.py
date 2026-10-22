"""Reproduce the verified Weixin 4.1.15.13 classic emoji extraction.

Offline data parsing only: never loads or executes the source DLL.
Requires pefile, capstone and Pillow installed in the selected Python environment.
"""
from pathlib import Path
import argparse
import hashlib
import io
import json
import struct
import xml.etree.ElementTree as ET
import zlib

ROOT = Path(__file__).resolve().parents[2]
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("source", type=Path, help="Path to the verified Weixin 4.1.15.13 Weixin.dll (only needed to reproduce extraction)")
SOURCE = parser.parse_args().source
SOURCE_HASH = "10f8e995453e2da46d4f2b5080cd6da1f13cc5147746adc119ceae38cb039de5"
ASSETS = ROOT / "assets" / "wechat-pet" / "emoji"
EVIDENCE = Path(__file__).resolve().parent
data = SOURCE.read_bytes()
assert hashlib.sha256(data).hexdigest() == SOURCE_HASH, "Unsupported source binary"
pe = pefile.PE(data=data, fast_load=True)
image_base = pe.OPTIONAL_HEADER.ImageBase


def offset(rva):
    return pe.get_offset_from_rva(rva)


def pointer(rva):
    return struct.unpack_from("<Q", data, offset(rva))[0] - image_base


def cstring(rva):
    start = offset(rva)
    end = data.index(b"\0", start, start + 256)
    return data[start:end].decode("utf-8")


def save_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


# Exact machine code is retained as evidence, not inferred from visual order.
checks = {
    0x3D19572: "4531ff",                 # Initial index i = 0.
    0x3D195CD: "4983ff69",               # i < 105.
    0x3D19660: "488d0d41738505",         # expression_%1.png format address.
    0x3D19676: "4d8d4701",               # Filename argument i + 1.
    0x3D19690: "4c8945e0",               # Save next outer index.
    0x3D196EC: "4c897db8",               # Save original i for language loops.
    0x3D1975D: "488d0d60728505",         # Language array relative-pointer table.
    0x3D19764: "4a6304b9",               # Read selected table offset.
    0x3D19768: "4801c8",                 # Resolve selected table address.
    0x3D1976B: "488b4db8",               # Original i.
    0x3D1976F: "4c8b2cc8",               # Name = selected table[i].
    0x3D197C3: "488d050e728505",         # Language name table.
}
for rva, expected in checks.items():
    actual = data[offset(rva):offset(rva) + len(expected) // 2].hex()
    assert actual == expected, (hex(rva), actual, expected)
assert cstring(0x95709A8) == ":/emoji/expression_%1.png"
language_tables = []
for index in range(4):
    delta = struct.unpack_from("<i", data, offset(0x95709C4) + index * 4)[0]
    language_tables.append({
        "language": cstring(pointer(0x95709D8 + index * 8)),
        "arrayRva": 0x95709C4 + delta,
    })
zh_table = next(item["arrayRva"] for item in language_tables if item["language"] == "zh_CN")
assert zh_table == 0x9572560

# Qt resource v2 table; every extracted PNG is copied byte-for-byte.
name_base, tree_base, data_base = 139264816, 139270272, 138165344
resources = {}
for index in range(154):
    node = tree_base + index * 22
    name_offset, flags = struct.unpack_from(">IH", data, node)
    name_start = name_base + name_offset
    name_length = struct.unpack_from(">H", data, name_start)[0]
    name = data[name_start + 6:name_start + 6 + name_length * 2].decode("utf-16-be")
    if flags & 2:
        continue
    payload = data_base + struct.unpack_from(">I", data, node + 10)[0]
    encoded_length = struct.unpack_from(">I", data, payload)[0]
    assert encoded_length < 2_000_000
    image_bytes = data[payload + 4:payload + 4 + encoded_length]
    if flags == 1:
        decoded_length = struct.unpack_from(">I", image_bytes, 0)[0]
        image_bytes = zlib.decompress(image_bytes[4:])
        assert len(image_bytes) == decoded_length
    else:
        assert flags == 0
    assert name not in resources
    resources[name] = (image_bytes, node, payload, encoded_length, flags)
assert len(resources) == 152
for name in ("newemoji-config.xml", "emojipanel-config.xml"):
    assert resources[name][0] == (EVIDENCE / name).read_bytes()

classic = []
aliases = json.loads((ASSETS / "native-aliases.json").read_text(encoding="utf-8-sig"))
original_aliases = dict(aliases)
for index in range(105):
    name_rva = pointer(zh_table + index * 8)
    alias = cstring(name_rva)
    assert alias.startswith("[") and alias.endswith("]")
    name = alias[1:-1]
    file_name = f"expression_{index + 1}.png"
    payload, node, position, length, flags = resources[file_name]
    with Image.open(io.BytesIO(payload)) as image:
        image.load()
        width, height = image.size
        assert image.format == "PNG" and width > 0 and height > 0
    existing = aliases.get(name)
    assert existing is None or existing == file_name, (name, existing, file_name)
    aliases[name] = file_name
    (ASSETS / "native" / file_name).write_bytes(payload)
    classic.append({
        "index": index,
        "alias": alias,
        "file": file_name,
        "output": "assets/wechat-pet/emoji/native/" + file_name,
        "namePointerTableEntryRva": zh_table + index * 8,
        "nameRva": name_rva,
        "nameFileOffset": offset(name_rva),
        "wireKey": cstring(pointer(0x9570660 + index * 8)),
        "width": width,
        "height": height,
        "nodeOffset": node,
        "dataOffset": position,
        "encodedBytes": length,
        "flags": flags,
        "sha256": hashlib.sha256(payload).hexdigest(),
    })
assert classic[2]["alias"] == "[色]" and classic[12]["alias"] == "[调皮]"
assert len({row["alias"] for row in classic}) == 105

xml = ET.fromstring(resources["newemoji-config.xml"][0])
modern = [{"alias": entry.findtext("cn-value"), "file": entry.findtext("fileName")}
          for entry in xml.findall("emoji")]
assert len(modern) == 45
names = list(dict.fromkeys(row["alias"] for row in classic + modern))
assert len(names) == 147
assert len(aliases) == 115
for alias, file_name in original_aliases.items():
    assert aliases[alias] == file_name
save_json(ASSETS / "native-aliases.json", aliases)
save_json(ASSETS / "native-names.json", names)

cs = Cs(CS_ARCH_X86, CS_MODE_64)
instructions = []
for start, end in ((0x3D19520, 0x3D19578), (0x3D195C9, 0x3D1969C), (0x3D196E0, 0x3D197D1)):
    for ins in cs.disasm(data[offset(start):offset(end)], start):
        instructions.append({"rva": hex(ins.address), "fileOffset": offset(ins.address),
                             "bytes": ins.bytes.hex(), "mnemonic": ins.mnemonic, "operands": ins.op_str})
proof = {
    "source": str(SOURCE), "sourceSHA256": SOURCE_HASH,
    "method": "Offline PE data and x86-64 instruction decoding; no target execution or UI access",
    "functionRva": "0x3d19520", "functionEndRva": "0x3d19dcf",
    "mappingRule": "For i=0..104, resource template expression_%1.png receives i+1; the same i reads zh_CN[i].",
    "templateRva": "0x95709a8", "template": cstring(0x95709A8),
    "languageTables": language_tables, "instructions": instructions,
    "classicCount": len(classic), "classicMappings": classic,
    "modernConfigCount": len(modern), "modernMappings": modern,
    "duplicateNamesAcrossTables": sorted(set(row["alias"] for row in classic) & set(row["alias"] for row in modern)),
    "duplicateResolution": "Preview retains the directly mapped classic image; modern override order was not inferred.",
    "supportedNameCount": len(names), "supportedNamesAsset": "assets/wechat-pet/emoji/native-names.json",
    "namesScope": "Compiled zh_CN dictionary plus newemoji-config cn-value, not a claim all 147 names appear in the visible panel.",
    "previewMappingCount": len(aliases), "allExtractedPngsFullyDecoded": True,
}
save_json(EVIDENCE / "classic-mapping-evidence.json", proof)
evidence = json.loads((EVIDENCE / "evidence.json").read_text(encoding="utf-8-sig"))
evidence["records"] = [row for row in evidence["records"] if not row["file"].startswith("expression_")] + classic
evidence["mappingSource"] = "newemoji-config.xml for 10 modern images; decoded function RVA 0x3d19520 and zh_CN pointer table for 105 classic images"
evidence["unverifiedClassicAliases"] = []
evidence["note"] = "Personal-use original PNG bytes; all production mappings are explicit config entries or proven same-index machine-code mapping."
evidence["classicInvestigation"] = {
    "status": "verified-static-dataflow", "proof": "classic-mapping-evidence.json",
    "classicMappings": 105, "supportNameCount": 147,
    "decoder": "Existing local Capstone; static binary bytes only",
}
save_json(EVIDENCE / "evidence.json", evidence)
print(json.dumps({"classicImages": len(classic), "modernImages": 10, "nativeMappings": len(aliases),
                  "supportedNames": len(names), "pngSizes": sorted({(row["width"], row["height"]) for row in classic}),
                  "evidence": str(EVIDENCE / "classic-mapping-evidence.json")}, ensure_ascii=False))

