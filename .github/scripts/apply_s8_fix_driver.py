from __future__ import annotations

import argparse
import contextlib
import hashlib
import json
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator, Sequence

ROOT = Path(__file__).resolve().parents[2]
SCRIPT_DIR = ROOT / ".github" / "scripts"
PATCH_PATH = ROOT / ".github" / "patches" / "s8-cross-step-bound-city.patch"
MANIFEST_PATH = ROOT / ".github" / "patches" / "s8-cross-step-bound-city.manifest.json"
LEGACY_PATH = SCRIPT_DIR / "apply_s8_fix_legacy_1549d62.py"


@dataclass(frozen=True)
class EolState:
    path: Path
    style: str
    had_utf8_bom: bool


def _canonical_lf(data: bytes) -> bytes:
    return data.replace(b"\r\n", b"\n").replace(b"\r", b"\n")


def _sha256_canonical_lf(data: bytes) -> str:
    return hashlib.sha256(_canonical_lf(data)).hexdigest()


def _detect_eol(data: bytes) -> str:
    body = data[3:] if data.startswith(b"\xef\xbb\xbf") else data
    canonical = _canonical_lf(body)
    if b"\r\n" in body and canonical.replace(b"\n", b"\r\n") == body:
        return "crlf"
    if b"\r" in body and b"\n" not in body:
        return "cr"
    return "lf"


def _encode_eol(data: bytes, style: str) -> bytes:
    had_bom = data.startswith(b"\xef\xbb\xbf")
    body = data[3:] if had_bom else data
    body = _canonical_lf(body)
    if style == "crlf":
        body = body.replace(b"\n", b"\r\n")
    elif style == "cr":
        body = body.replace(b"\n", b"\r")
    elif style != "lf":
        raise ValueError(f"Unsupported EOL style: {style}")
    return (b"\xef\xbb\xbf" if had_bom else b"") + body


def _write_lf(path: Path) -> EolState:
    data = path.read_bytes()
    state = EolState(
        path=path,
        style=_detect_eol(data),
        had_utf8_bom=data.startswith(b"\xef\xbb\xbf"),
    )
    path.write_bytes(_encode_eol(data, "lf"))
    return state


def _restore_eol(state: EolState) -> None:
    if not state.path.exists():
        return
    data = state.path.read_bytes()
    restored = _encode_eol(data, state.style)
    if state.had_utf8_bom and not restored.startswith(b"\xef\xbb\xbf"):
        restored = b"\xef\xbb\xbf" + restored
    if not state.had_utf8_bom and restored.startswith(b"\xef\xbb\xbf"):
        restored = restored[3:]
    state.path.write_bytes(restored)


@contextlib.contextmanager
def _canonical_eol_transaction(paths: Sequence[Path]) -> Iterator[None]:
    states: list[EolState] = []
    try:
        for path in paths:
            if path.is_file():
                states.append(_write_lf(path))
        yield
    finally:
        for state in states:
            _restore_eol(state)


def _load_manifest() -> dict:
    payload = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    if payload.get("format") != 1:
        raise SystemExit(f"Unsupported S8 patch manifest format: {payload.get('format')!r}")
    if payload.get("historical_patcher_commit") != "1549d62":
        raise SystemExit("S8 patch manifest is not tied to historical baseline 1549d62")
    return payload


def _manifest_paths(manifest: dict) -> list[Path]:
    result: list[Path] = []
    for item in manifest["files"]:
        path = ROOT / item["path"]
        if not path.is_file():
            raise SystemExit(f"S8 patch target is missing: {item['path']}")
        result.append(path)
    return result


def _classify(manifest: dict) -> tuple[str, list[str]]:
    states: list[str] = []
    details: list[str] = []
    for item in manifest["files"]:
        path = ROOT / item["path"]
        actual = _sha256_canonical_lf(path.read_bytes()) if path.is_file() else "<missing>"
        before = item["before_sha256_canonical_lf"]
        after = item["after_sha256_canonical_lf"]
        if actual == before:
            state = "before"
        elif actual == after:
            state = "after"
        else:
            state = "unexpected"
        states.append(state)
        details.append(f"{item['path']}: {state} actual={actual} before={before} after={after}")

    unique = set(states)
    if unique == {"before"}:
        return "before", details
    if unique == {"after"}:
        return "after", details
    return "mixed-or-unexpected", details


def _run(command: Sequence[str], *, check: bool = True) -> subprocess.CompletedProcess[str]:
    print("+", " ".join(command), flush=True)
    return subprocess.run(
        list(command),
        cwd=ROOT,
        check=check,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


def apply_source_patch() -> None:
    manifest = _load_manifest()
    classification, details = _classify(manifest)
    if classification == "after":
        print("S8 function-context source patch is already applied; no changes made.")
        return
    if classification != "before":
        raise SystemExit(
            "S8 source tree is neither the audited preimage nor the audited postimage.\n"
            + "\n".join(details)
        )

    targets = _manifest_paths(manifest)
    with _canonical_eol_transaction(targets):
        _run(
            [
                "git",
                "apply",
                "--recount",
                "--whitespace=nowarn",
                "--unsafe-paths",
                str(PATCH_PATH),
            ]
        )
        post_classification, post_details = _classify(manifest)
        if post_classification != "after":
            raise SystemExit(
                "S8 patch applied but canonical postimage verification failed.\n"
                + "\n".join(post_details)
            )

    final_classification, final_details = _classify(manifest)
    if final_classification != "after":
        raise SystemExit(
            "S8 patch postimage changed while restoring Windows line endings.\n"
            + "\n".join(final_details)
        )
    _run(["git", "diff", "--check"])
    print("S8 function-context source patch applied and verified.")


def run_legacy_transaction(arguments: Sequence[str]) -> None:
    if not LEGACY_PATH.is_file():
        raise SystemExit(f"Recovered plaintext baseline is missing: {LEGACY_PATH}")

    manifest = _load_manifest()
    paths = _manifest_paths(manifest)
    for relative in ("build.ps1", "STATE.md"):
        candidate = ROOT / relative
        if candidate.is_file() and candidate not in paths:
            paths.append(candidate)

    with _canonical_eol_transaction(paths):
        _run([sys.executable, str(LEGACY_PATH), *arguments])

    _run(["git", "diff", "--check"])


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Apply the audited S8 source patch idempotently, or delegate output/state "
            "bookkeeping to the recovered plaintext 1549d62 baseline inside an LF-canonical transaction."
        )
    )
    parser.add_argument("command", choices=("source", "outputs", "state"))
    parser.add_argument("remainder", nargs=argparse.REMAINDER)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    if args.command == "source":
        if args.remainder:
            raise SystemExit("The source command accepts no extra arguments")
        apply_source_patch()
    else:
        run_legacy_transaction([args.command, *args.remainder])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
