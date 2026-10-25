"""Build the OFL-derived Token Grove Rounded font using Python's standard library.

Usage: python prepare_font.py original-ChillRoundFRegular.ttf GroveRoundF-Regular.ttf
Glyph outlines are unchanged. Font names and grayscale rendering preferences change.
"""
import hashlib
import math
import pathlib
import struct
import sys
import time


def checksum(data):
    data = bytes(data) + b"\0" * (-len(data) % 4)
    return sum(struct.unpack(">" + "I" * (len(data) // 4), data)) & 0xFFFFFFFF


source = pathlib.Path(sys.argv[1]).read_bytes()
count = struct.unpack_from(">H", source, 4)[0]
tables = {}
for i in range(count):
    tag, _, offset, length = struct.unpack_from(">4sIII", source, 12 + i * 16)
    tables[tag] = source[offset:offset + length]
original_glyphs = hashlib.sha256(tables[b"glyf"]).digest()

old_name = tables[b"name"]
version, name_count, storage = struct.unpack_from(">HHH", old_name)
if version != 0:
    raise ValueError("Expected a format-0 name table")
records, strings, shared = [], bytearray(), {}
for i in range(name_count):
    platform, encoding, language, name_id, length, offset = struct.unpack_from(">6H", old_name, 6 + i * 12)
    raw = old_name[storage + offset:storage + offset + length]
    codec = "utf-16-be" if platform in (0, 3) else "mac_roman"
    text = raw.decode(codec)
    chinese = platform == 3 and language in (0x804, 0x404, 0xC04, 0x1004, 0x1404)
    family = "小站萌圆体" if chinese else "Token Grove Rounded"
    if name_id in (1, 16, 21):
        text = family
    elif name_id in (4, 18):
        text = family + (" Regular" if not chinese else "")
    elif name_id in (6, 25):
        text = "TokenGroveRounded-Regular"
    elif name_id == 3:
        text = "TokenGroveRounded-3.201"
    elif name_id == 5:
        text = "Version 3.201; Token Grove grayscale edition"
    raw = text.encode(codec)
    if raw not in shared:
        shared[raw] = len(strings)
        strings.extend(raw)
    records.append(struct.pack(">6H", platform, encoding, language, name_id, len(raw), shared[raw]))
tables[b"name"] = struct.pack(">3H", 0, name_count, 6 + name_count * 12) + b"".join(records) + strings
# The upstream has no gasp table, so Windows can choose monochrome at UI sizes.
tables[b"gasp"] = struct.pack(">4H", 1, 1, 0xFFFF, 0x0003)
tables.pop(b"DSIG", None)  # A signature cannot apply to a modified font.
head = bytearray(tables[b"head"])
struct.pack_into(">I", head, 4, round(3.201 * 65536))
struct.pack_into(">I", head, 8, 0)
struct.pack_into(">Q", head, 28, int(time.time()) + 2082844800)
tables[b"head"] = head

count = len(tables)
power = int(math.log2(count))
header = struct.pack(">4s4H", source[:4], count, 16 * 2**power, power, 16 * count - 16 * 2**power)
directory, body = bytearray(), bytearray()
offset = 12 + count * 16
head_offset = None
for tag, data in sorted(tables.items()):
    directory.extend(struct.pack(">4sIII", tag, checksum(data), offset, len(data)))
    if tag == b"head":
        head_offset = offset
    padded = bytes(data) + b"\0" * (-len(data) % 4)
    body.extend(padded)
    offset += len(padded)
output = bytearray(header + directory + body)
struct.pack_into(">I", output, head_offset + 8, (0xB1B0AFBA - checksum(output)) & 0xFFFFFFFF)
assert checksum(output) == 0xB1B0AFBA
assert hashlib.sha256(tables[b"glyf"]).digest() == original_glyphs
pathlib.Path(sys.argv[2]).write_bytes(output)
print("Built Token Grove Rounded; outlines unchanged; grayscale enabled; checksum valid.")
