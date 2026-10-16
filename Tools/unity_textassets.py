"""Minimal reader for Unity SerializedFile (*.assets) that lists/extracts TextAsset objects.

Only supports what RimWorld 1.6 (Unity 2022.3, SerializedFile format 22) needs.
Usage:
    python -I unity_textassets.py <file.assets>            # list TextAssets
"""
import struct
import sys

TEXT_ASSET_CLASS_ID = 49


class _Reader:
    def __init__(self, data, little=True):
        self.data = data
        self.pos = 0
        self.e = '<' if little else '>'

    def u8(self):
        v = self.data[self.pos]
        self.pos += 1
        return v

    def _unpack(self, fmt, size):
        v = struct.unpack_from(self.e + fmt, self.data, self.pos)[0]
        self.pos += size
        return v

    def i16(self): return self._unpack('h', 2)
    def u16(self): return self._unpack('H', 2)
    def i32(self): return self._unpack('i', 4)
    def u32(self): return self._unpack('I', 4)
    def i64(self): return self._unpack('q', 8)

    def cstr(self):
        end = self.data.index(b'\0', self.pos)
        s = self.data[self.pos:end].decode('utf-8', 'replace')
        self.pos = end + 1
        return s

    def align(self, n=4):
        self.pos = (self.pos + n - 1) // n * n

    def skip(self, n):
        self.pos += n


def read_text_assets(path):
    """Yield (path_id, name, text) for every TextAsset in a SerializedFile."""
    data = open(path, 'rb').read()
    hdr = _Reader(data, little=False)
    hdr.u32()  # metadata size (legacy)
    hdr.u32()  # file size (legacy)
    version = hdr.u32()
    data_offset = hdr.u32()
    if version < 9:
        raise ValueError(f'Unsupported SerializedFile version {version}')
    little = hdr.u8() == 0
    hdr.skip(3)
    if version >= 22:
        hdr.u32()  # metadata size
        hdr.i64()  # file size
        data_offset = hdr.i64()
        hdr.i64()
    r = _Reader(data, little)
    r.pos = hdr.pos
    r.cstr()  # unity version
    r.i32()  # target platform
    enable_type_tree = r.u8() != 0
    class_ids = []
    for _ in range(r.i32()):
        class_id = r.i32()
        if version >= 16:
            r.u8()  # is stripped
        if version >= 17:
            r.i16()  # script type index
        if (version >= 16 and class_id == 114) or (version < 16 and class_id < 0):
            r.skip(16)  # script id
        r.skip(16)  # old type hash
        if enable_type_tree:
            node_count = r.i32()
            string_size = r.i32()
            r.skip(node_count * (32 if version >= 19 else 24) + string_size)
            if version >= 21:
                r.skip(4 * r.i32())  # type dependencies
        class_ids.append(class_id)
    if 7 <= version < 14:
        r.i32()
    for _ in range(r.i32()):
        if version >= 14:
            r.align(4)
        path_id = r.i64()
        start = (r.i64() if version >= 22 else r.u32()) + data_offset
        r.u32()  # byte size
        type_index = r.i32()
        if class_ids[type_index] != TEXT_ASSET_CLASS_ID:
            continue
        o = _Reader(data, little)
        o.pos = start
        n = o.i32()
        name = data[o.pos:o.pos + n].decode('utf-8', 'replace')
        o.pos += n
        o.align(4)
        n = o.i32()
        text = data[o.pos:o.pos + n].decode('utf-8-sig', 'replace')
        yield path_id, name, text


if __name__ == '__main__':
    for pid, name, text in read_text_assets(sys.argv[1]):
        print(f'{pid}\t{name}\t{len(text)}')
