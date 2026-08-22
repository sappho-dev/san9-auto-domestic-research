#!/usr/bin/env python3
"""Offline mutation/self-test for san9_easy_manifest.py."""

from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
from typing import Callable

import pefile

import san9_easy_manifest as subject


class TestFailure(RuntimeError):
    pass


class Counter:
    def __init__(self) -> None:
        self.passed = 0

    def check(self, condition: bool, label: str) -> None:
        if not condition:
            raise TestFailure(label)
        self.passed += 1

    def rejects(self, action: Callable[[], object], label: str) -> None:
        try:
            action()
        except (subject.ManifestError, pefile.PEFormatError, OSError, ValueError):
            self.passed += 1
            return
        raise TestFailure(f"expected rejection: {label}")


def _flip_hex(value: str) -> str:
    raw = bytearray.fromhex(value)
    if not raw:
        raise TestFailure("cannot mutate empty hex")
    raw[0] ^= 0x01
    return raw.hex().upper()


def _binary_mutation_tests(
    counter: Counter,
    manifest: dict,
    game_source: Path,
    loader_source: Path,
    easy_source: Path,
) -> None:
    game_data = game_source.read_bytes()
    loader_data = loader_source.read_bytes()
    easy_data = easy_source.read_bytes()

    def mutate_identity(name: str, baseline: bytes, offsets: list[int], label: str) -> None:
        subject._validate_blob_identity(name, baseline)
        counter.passed += 1
        for index, offset in enumerate(offsets):
            changed = bytearray(baseline)
            changed[offset] ^= 0x01
            counter.rejects(
                lambda changed=changed: subject._validate_blob_identity(name, bytes(changed)),
                f"{label} single-byte mutation {index}",
            )

    game_pe = pefile.PE(data=game_data, fast_load=False)
    game_offsets: list[int] = []
    for item in manifest["redirect_writes"] + manifest["auxiliary_writes"]:
        if item["target_module"] == "San9PK.exe":
            game_offsets.append(game_pe.get_offset_from_rva(int(item["target_rva"], 16)))
    counter.check(len(game_offsets) == 37 and len(set(game_offsets)) == 37, "37 unique game anchors")
    mutate_identity("San9PK.exe", game_data, game_offsets, "game anchor")

    loader_pe = pefile.PE(data=loader_data, fast_load=False)
    loader_rvas = [
        site - loader_pe.OPTIONAL_HEADER.ImageBase
        for site in subject.WPM_IMPORT_XREFS + subject.VIRTUAL_PROTECT_XREFS
    ] + subject.REDIRECT_DESCRIPTOR_RVAS
    loader_offsets = [loader_pe.get_offset_from_rva(rva) for rva in loader_rvas]
    counter.check(len(loader_offsets) == 39, "39 loader write-closure mutation sites")
    mutate_identity("San9PKEasy.exe", loader_data, loader_offsets, "loader closure")

    easy_pe = pefile.PE(data=easy_data, fast_load=False)
    easy_rvas = [0x1000 + index * 10 + 6 for index in range(26)] + [0x6450]
    easy_offsets = [easy_pe.get_offset_from_rva(rva) for rva in easy_rvas]
    counter.check(len(easy_offsets) == 27, "27 Easy initializer/slot mutation sites")
    mutate_identity("Easy.dll", easy_data, easy_offsets, "Easy table/slot")


def _manifest_mutation_tests(
    counter: Counter, manifest: dict, game: Path, loader: Path, easy: Path
) -> None:
    for index, item in enumerate(manifest["redirect_writes"]):
        changed = copy.deepcopy(manifest)
        changed["redirect_writes"][index]["original_hex"] = _flip_hex(item["original_hex"])
        counter.rejects(
            lambda changed=changed: subject.validate_manifest(changed, game, loader, easy),
            f"manifest redirect original mutation {index}",
        )
        changed = copy.deepcopy(manifest)
        target = int(item["installed_template"]["target_rva"], 16) ^ 1
        changed["redirect_writes"][index]["installed_template"]["target_rva"] = subject._hx(target)
        counter.rejects(
            lambda changed=changed: subject.validate_manifest(changed, game, loader, easy),
            f"manifest redirect target mutation {index}",
        )
    for index, item in enumerate(manifest["auxiliary_writes"]):
        changed = copy.deepcopy(manifest)
        changed["auxiliary_writes"][index]["original_hex"] = _flip_hex(item["original_hex"])
        counter.rejects(
            lambda changed=changed: subject.validate_manifest(changed, game, loader, easy),
            f"manifest auxiliary mutation {index}",
        )


def _runtime_snapshot_tests(counter: Counter, manifest: dict) -> None:
    original = subject.synthesize_runtime_snapshot(manifest, 0x10000000, "original")
    installed_preferred = subject.synthesize_runtime_snapshot(manifest, 0x10000000, "installed")
    installed_relocated = subject.synthesize_runtime_snapshot(manifest, 0x22000000, "installed")
    installed_high = subject.synthesize_runtime_snapshot(manifest, 0x90000000, "installed")
    installed_very_high = subject.synthesize_runtime_snapshot(manifest, 0xF0000000, "installed")
    counter.check(subject.validate_runtime_snapshot(manifest, original) == "original", "original snapshot")
    original_training_zero = copy.deepcopy(original)
    original_training_zero["points"]["aux-child-training-a"] = "00"
    original_training_zero["points"]["aux-child-training-b"] = "00"
    counter.rejects(
        lambda: subject.validate_runtime_snapshot(manifest, original_training_zero),
        "original redirects with non-restored training 00/00",
    )
    counter.check(
        subject.validate_runtime_snapshot(manifest, installed_preferred) == "installed",
        "preferred-base installed snapshot",
    )
    counter.check(
        subject.validate_runtime_snapshot(manifest, installed_relocated) == "installed",
        "relocated-base installed snapshot",
    )
    counter.check(
        subject.validate_runtime_snapshot(manifest, installed_high) == "installed",
        "high-bit 0x90000000 modulo-rel32 installed snapshot",
    )
    counter.check(
        subject.validate_runtime_snapshot(manifest, installed_very_high) == "installed",
        "high-bit 0xF0000000 modulo-rel32 installed snapshot",
    )
    overflowing_image = subject.synthesize_runtime_snapshot(manifest, 0xFFFFA000, "installed")
    counter.rejects(
        lambda: subject.validate_runtime_snapshot(manifest, overflowing_image),
        "Easy base whose full SizeOfImage wraps beyond UInt32",
    )
    training_off = copy.deepcopy(installed_relocated)
    training_off["points"]["aux-child-training-a"] = "00"
    training_off["points"]["aux-child-training-b"] = "00"
    counter.check(
        subject.validate_runtime_snapshot(manifest, training_off) == "installed",
        "paired training 00/00 state",
    )

    changed_manifest = copy.deepcopy(manifest)
    changed_manifest["redirect_writes"][0]["target_va"] = "0x004E6271"
    changed_manifest_snapshot = subject.synthesize_runtime_snapshot(
        changed_manifest, 0x22000000, "installed"
    )
    counter.rejects(
        lambda: subject.validate_runtime_snapshot(changed_manifest, changed_manifest_snapshot),
        "direct runtime validation with same-shape changed redirect target_va",
    )

    for item in manifest["redirect_writes"] + manifest["auxiliary_writes"]:
        changed = copy.deepcopy(installed_relocated)
        changed["points"][item["id"]] = _flip_hex(changed["points"][item["id"]])
        counter.rejects(
            lambda changed=changed: subject.validate_runtime_snapshot(manifest, changed),
            f"runtime point mutation {item['id']}",
        )
        missing = copy.deepcopy(installed_relocated)
        del missing["points"][item["id"]]
        counter.rejects(
            lambda missing=missing: subject.validate_runtime_snapshot(manifest, missing),
            f"runtime missing point {item['id']}",
        )

    split = copy.deepcopy(installed_relocated)
    split["points"]["aux-child-training-a"] = "00"
    split["points"]["aux-child-training-b"] = "32"
    counter.rejects(lambda: subject.validate_runtime_snapshot(manifest, split), "split training pair")

    mixed = copy.deepcopy(installed_relocated)
    mixed["points"]["redirect-00"] = manifest["redirect_writes"][0]["original_hex"]
    counter.rejects(lambda: subject.validate_runtime_snapshot(manifest, mixed), "mixed redirect state")

    short_redirect = copy.deepcopy(installed_relocated)
    short_redirect["points"]["redirect-00"] = "E9"
    counter.rejects(
        lambda: subject.validate_runtime_snapshot(manifest, short_redirect),
        "short rel32 byte sequence",
    )

    wrong_target = copy.deepcopy(installed_relocated)
    first = manifest["redirect_writes"][0]
    wrong_target["points"]["redirect-00"] = subject._installed_bytes(
        first["instruction_type"],
        int(first["target_va"], 16),
        0x22000000,
        int(first["installed_template"]["target_rva"], 16) + 1,
    ).hex().upper()
    counter.rejects(
        lambda: subject.validate_runtime_snapshot(manifest, wrong_target),
        "rel32 resolves to wrong Easy target RVA",
    )

    wrong_base = copy.deepcopy(installed_relocated)
    wrong_base["easy_module_base"] = "0x23000000"
    counter.rejects(lambda: subject.validate_runtime_snapshot(manifest, wrong_base), "wrong Easy base")

    wrong_hwnd = copy.deepcopy(installed_relocated)
    wrong_hwnd["exact_game_hwnd"] = "0x00123457"
    counter.rejects(lambda: subject.validate_runtime_snapshot(manifest, wrong_hwnd), "wrong bound HWND")

    wrong_page = copy.deepcopy(installed_relocated)
    wrong_page["page_protections"]["page-protection-easy-global-strings"] = "0x02"
    counter.rejects(lambda: subject.validate_runtime_snapshot(manifest, wrong_page), "wrong page protection")

    extra = copy.deepcopy(installed_relocated)
    extra["points"]["unknown"] = "00"
    counter.rejects(lambda: subject.validate_runtime_snapshot(manifest, extra), "unknown extra point")

    bad_base_type = copy.deepcopy(installed_relocated)
    bad_base_type["easy_module_base"] = 0x22000000
    counter.rejects(
        lambda: subject.validate_runtime_snapshot(manifest, bad_base_type), "non-string Easy base"
    )

    bad_point_type = copy.deepcopy(installed_relocated)
    bad_point_type["points"]["redirect-00"] = 0
    counter.rejects(
        lambda: subject.validate_runtime_snapshot(manifest, bad_point_type), "non-string point bytes"
    )

    bad_page_type = copy.deepcopy(installed_relocated)
    bad_page_type["page_protections"]["page-protection-easy-global-strings"] = 4
    counter.rejects(
        lambda: subject.validate_runtime_snapshot(manifest, bad_page_type), "non-string page protection"
    )


def main() -> int:
    counter = Counter()
    game, loader, easy = subject.DEFAULT_GAME, subject.DEFAULT_LOADER, subject.DEFAULT_EASY
    manifest_path = subject.DEFAULT_MANIFEST
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    reconstructed = subject.validate_manifest(manifest, game, loader, easy)
    counter.check(subject._canonical(manifest) == subject._canonical(reconstructed), "baseline reconstruction")
    counter.check(
        hashlib.sha256(manifest_path.read_bytes()).hexdigest().upper()
        == hashlib.sha256((json.dumps(reconstructed, ensure_ascii=False, indent=2) + "\n").encode("utf-8")).hexdigest().upper(),
        "deterministic serialized manifest",
    )
    _manifest_mutation_tests(counter, manifest, game, loader, easy)
    _runtime_snapshot_tests(counter, manifest)
    _binary_mutation_tests(counter, manifest, game, loader, easy)
    print(f"PASS {counter.passed}/{counter.passed} offline tests; process access=0; writes to targets=0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
