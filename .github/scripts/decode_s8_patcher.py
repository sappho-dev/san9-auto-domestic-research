from __future__ import annotations

import base64
import gzip
import hashlib
import zlib
from pathlib import Path

SOURCE = Path(__file__).with_name("apply_s8_fix.py.gz.b64")
TARGET = Path(__file__).with_name("apply_s8_fix.decoded.py")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main() -> None:
    raw = SOURCE.read_bytes()
    print(f"source_size={len(raw)} source_sha256={sha256(raw)}")

    text = raw.decode("utf-8-sig")
    encoded = "".join(text.split())
    encoded += "=" * (-len(encoded) % 4)
    payload = base64.b64decode(encoded, validate=False)
    print(
        f"payload_size={len(payload)} "
        f"payload_magic={payload[:16].hex()} "
        f"payload_sha256={sha256(payload)}"
    )

    if payload.startswith(b"\x1f\x8b"):
        decoded = gzip.decompress(payload)
        codec = "gzip"
    elif payload.startswith((b"\x78\x01", b"\x78\x5e", b"\x78\x9c", b"\x78\xda")):
        decoded = zlib.decompress(payload)
        codec = "zlib"
    else:
        decoded = payload
        codec = "raw"

    print(
        f"codec={codec} decoded_size={len(decoded)} "
        f"decoded_magic={decoded[:16].hex()} "
        f"decoded_sha256={sha256(decoded)}"
    )
    TARGET.write_bytes(decoded)


if __name__ == "__main__":
    main()
