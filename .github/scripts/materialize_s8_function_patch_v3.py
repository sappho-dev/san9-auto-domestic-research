from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import os
import py_compile
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

BASELINE = "1549d62"
BASELINE_COPY = Path(".github/scripts/apply_s8_fix.baseline_1549d62.py")
PATCH_PATH = Path(".github/patches/s8-cross-step-bound-city.function-context.patch")
RECOVERY_NOTE = Path(".github/scripts/apply_s8_fix.recovery.txt")
REQUIRED_MARKERS = (
    b"BATCH_REBIND_REQUIRED",
    b"s5_current_context",
    b"controller.c",
    b"bridge_dll.c",
    b"offline_selftest.c",
)
ALLOWED_SOURCE_PATHS = {
    "build.ps1",
    "native/San9BridgeP1EasyPingM2b/include/s5_current_context.h",
    "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c",
    "native/San9BridgeP1EasyPingM2b/src/controller.c",
    "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c",
    "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c",
    "native/San9BridgeP1EasyPingM2b/build.ps1",
    "src/San9AutoDomestic.UI/NativeControllerClient.cs",
    "tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs",
}


@dataclass(frozen=True)
class RecoveredCandidate:
    source_path: str
    container_kind: str
    container_sha256: str
    plaintext: bytes

    @property
    def plaintext_sha256(self) -> str:
        return hashlib.sha256(self.plaintext).hexdigest()


def run(
    args: list[str],
    *,
    cwd: Path,
    check: bool = True,
    capture: bool = False,
) -> subprocess.CompletedProcess[bytes]:
    env = os.environ.copy()
    env.update(
        {
            "GIT_CONFIG_COUNT": "3",
            "GIT_CONFIG_KEY_0": "core.autocrlf",
            "GIT_CONFIG_VALUE_0": "false",
            "GIT_CONFIG_KEY_1": "core.safecrlf",
            "GIT_CONFIG_VALUE_1": "false",
            "GIT_CONFIG_KEY_2": "core.eol",
            "GIT_CONFIG_VALUE_2": "lf",
        }
    )
    return subprocess.run(
        args,
        cwd=cwd,
        env=env,
        check=check,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.STDOUT if capture else None,
    )


def repo_root() -> Path:
    result = run(
        ["git", "rev-parse", "--show-toplevel"],
        cwd=Path.cwd(),
        capture=True,
    )
    return Path(result.stdout.decode("utf-8").strip()).resolve()


def decode_baseline_blob(path: str, blob: bytes) -> tuple[str, bytes] | None:
    lower = path.lower()
    if lower.endswith(".py"):
        return "plaintext-python", blob

    if lower.endswith((".gz.b64", ".b64", ".base64")):
        try:
            encoded = b"".join(blob.split())
            encoded += b"=" * (-len(encoded) % 4)
            payload = base64.b64decode(encoded, validate=True)
        except Exception:
            return None
        if payload.startswith(b"\x1f\x8b"):
            try:
                return "base64+gzip", gzip.decompress(payload)
            except Exception:
                return None
        return "base64", payload

    if lower.endswith(".gz") and blob.startswith(b"\x1f\x8b"):
        try:
            return "gzip", gzip.decompress(blob)
        except Exception:
            return None
    return None


def recover_baseline(root: Path) -> RecoveredCandidate:
    tree = run(
        ["git", "ls-tree", "-r", "--name-only", "-z", BASELINE],
        cwd=root,
        capture=True,
    ).stdout
    candidates: list[RecoveredCandidate] = []
    for raw_path in tree.split(b"\0"):
        if not raw_path:
            continue
        path = raw_path.decode("utf-8")
        lower = path.lower()
        if not lower.endswith((".py", ".gz.b64", ".b64", ".base64", ".gz")):
            continue
        blob = run(
            ["git", "show", f"{BASELINE}:{path}"],
            cwd=root,
            capture=True,
        ).stdout
        decoded = decode_baseline_blob(path, blob)
        if decoded is None:
            continue
        kind, plaintext = decoded
        if not all(marker in plaintext for marker in REQUIRED_MARKERS):
            continue
        try:
            compile(plaintext, f"{BASELINE}:{path}", "exec")
        except Exception:
            continue
        candidates.append(
            RecoveredCandidate(
                source_path=path,
                container_kind=kind,
                container_sha256=hashlib.sha256(blob).hexdigest(),
                plaintext=plaintext,
            )
        )

    unique: dict[str, RecoveredCandidate] = {}
    aliases: dict[str, list[str]] = {}
    for candidate in candidates:
        digest = candidate.plaintext_sha256
        unique.setdefault(digest, candidate)
        aliases.setdefault(digest, []).append(candidate.source_path)
    if len(unique) != 1:
        detail = "; ".join(
            f"{digest}: {', '.join(paths)}" for digest, paths in sorted(aliases.items())
        ) or "<none>"
        raise RuntimeError(
            f"Expected exactly one unique complete S8 patch plaintext in {BASELINE}; "
            f"found {len(unique)} unique candidates: {detail}"
        )

    digest, selected = next(iter(unique.items()))
    target = root / BASELINE_COPY
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(selected.plaintext)
    py_compile.compile(str(target), doraise=True)
    alias_text = ",".join(sorted(aliases[digest]))
    (root / RECOVERY_NOTE).write_text(
        "\n".join(
            (
                f"baseline={BASELINE}",
                f"source_path={selected.source_path}",
                f"source_aliases={alias_text}",
                f"container_kind={selected.container_kind}",
                f"container_sha256={selected.container_sha256}",
                f"plaintext_sha256={selected.plaintext_sha256}",
                f"plaintext_size={len(selected.plaintext)}",
                "recovery=exact immutable Git blob from 1549d62; current damaged payload unused",
                "",
            )
        ),
        encoding="utf-8",
        newline="\n",
    )
    return selected


def changed_paths(worktree: Path) -> set[str]:
    output = run(
        [
            "git",
            "status",
            "--porcelain=v1",
            "--untracked-files=no",
            "-z",
        ],
        cwd=worktree,
        capture=True,
    ).stdout
    paths: set[str] = set()
    fields = [field for field in output.split(b"\0") if field]
    index = 0
    while index < len(fields):
        field = fields[index]
        if len(field) < 4:
            raise RuntimeError(f"Malformed git status field: {field!r}")
        status = field[:2].decode("ascii", errors="replace")
        path = field[3:].decode("utf-8").replace("\\", "/")
        paths.add(path)
        if "R" in status or "C" in status:
            index += 1
            if index >= len(fields):
                raise RuntimeError("Malformed rename/copy entry in git status")
            paths.add(fields[index].decode("utf-8").replace("\\", "/"))
        index += 1
    return paths


def generate_patch(root: Path, baseline: RecoveredCandidate) -> bytes:
    with tempfile.TemporaryDirectory(prefix="san9-s8-target-") as temp_dir:
        worktree = Path(temp_dir) / "worktree"
        run(
            ["git", "worktree", "add", "--detach", str(worktree), "HEAD"],
            cwd=root,
        )
        try:
            script = worktree / BASELINE_COPY
            script.parent.mkdir(parents=True, exist_ok=True)
            script.write_bytes(baseline.plaintext)
            py_compile.compile(str(script), doraise=True)
            run([sys.executable, str(script), "source"], cwd=worktree)
            run(["git", "diff", "--check"], cwd=worktree)

            actual = changed_paths(worktree)
            unexpected = actual - ALLOWED_SOURCE_PATHS
            missing = ALLOWED_SOURCE_PATHS - actual
            if unexpected:
                raise RuntimeError(
                    "Recovered patcher changed non-allowlisted paths: "
                    + ", ".join(sorted(unexpected))
                )
            if missing:
                raise RuntimeError(
                    "Recovered patcher did not change expected paths: "
                    + ", ".join(sorted(missing))
                )

            patch = run(
                [
                    "git",
                    "diff",
                    "--binary",
                    "--full-index",
                    "--function-context",
                    "--no-ext-diff",
                    "--",
                    *sorted(ALLOWED_SOURCE_PATHS),
                ],
                cwd=worktree,
                capture=True,
            ).stdout
            if not patch.startswith(b"diff --git ") or b"@@" not in patch:
                raise RuntimeError("Generated S8 function-context patch is empty or malformed")
            if b"\r\n" in patch:
                raise RuntimeError("Generated function-context patch contains CRLF")
            return patch
        finally:
            run(
                ["git", "worktree", "remove", "--force", str(worktree)],
                cwd=root,
                check=False,
            )
            run(["git", "worktree", "prune"], cwd=root, check=False)


def git_apply_check(root: Path, patch: Path, *, reverse: bool) -> bool:
    args = ["git", "apply", "--check", "--recount", "--whitespace=nowarn"]
    if reverse:
        args.append("--reverse")
    args.append(str(patch))
    return run(args, cwd=root, check=False, capture=True).returncode == 0


def apply_patch_idempotently(root: Path, patch_bytes: bytes) -> str:
    patch = root / PATCH_PATH
    patch.parent.mkdir(parents=True, exist_ok=True)
    patch.write_bytes(patch_bytes)

    forward = git_apply_check(root, patch, reverse=False)
    reverse = git_apply_check(root, patch, reverse=True)
    if forward and reverse:
        raise RuntimeError("S8 patch is ambiguous: forward and reverse checks both pass")
    if reverse:
        return "already-applied"
    if not forward:
        diagnostic = run(
            ["git", "apply", "--check", "--recount", "--verbose", str(patch)],
            cwd=root,
            check=False,
            capture=True,
        ).stdout.decode("utf-8", errors="replace")
        raise RuntimeError("S8 function-context patch cannot be applied:\n" + diagnostic)

    run(
        ["git", "apply", "--recount", "--whitespace=nowarn", str(patch)],
        cwd=root,
    )
    run(["git", "diff", "--check"], cwd=root)
    if not git_apply_check(root, patch, reverse=True):
        raise RuntimeError("Reverse check failed after applying S8 patch")
    return "applied"


def verify_plaintext_hashes(root: Path) -> None:
    for relative in (BASELINE_COPY, PATCH_PATH, RECOVERY_NOTE):
        data = (root / relative).read_bytes()
        if b"\r\n" in data:
            raise RuntimeError(f"Generated plaintext artifact contains CRLF: {relative}")
        print(
            f"{relative.as_posix()} size={len(data)} "
            f"sha256={hashlib.sha256(data).hexdigest()}"
        )


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Recover the intact S8 source from 1549d62 and materialize an "
            "idempotent plaintext function-context patch."
        )
    )
    parser.add_argument("--generate-only", action="store_true")
    args = parser.parse_args()

    root = repo_root()
    baseline = recover_baseline(root)
    patch_bytes = generate_patch(root, baseline)
    patch = root / PATCH_PATH
    patch.parent.mkdir(parents=True, exist_ok=True)
    patch.write_bytes(patch_bytes)
    status = "generated-only"
    if not args.generate_only:
        status = apply_patch_idempotently(root, patch_bytes)
    verify_plaintext_hashes(root)
    print(
        f"S8 materialization status={status} baseline={BASELINE}:"
        f"{baseline.source_path} plaintext_sha256={baseline.plaintext_sha256} "
        f"patch_sha256={hashlib.sha256(patch_bytes).hexdigest()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
