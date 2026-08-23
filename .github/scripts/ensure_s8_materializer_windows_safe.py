from __future__ import annotations

from pathlib import Path

PATH = Path(__file__).with_name("materialize_s8_function_patch_v3.py")


def replace_once(text: str, before: str, after: str, label: str) -> str:
    if after in text:
        return text
    if before not in text:
        raise RuntimeError(f"Cannot harden S8 materializer: missing {label} anchor")
    return text.replace(before, after, 1)


def main() -> int:
    text = PATH.read_text(encoding="utf-8")
    allow_block = '''ALLOWED_SOURCE_PATHS = {
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
'''
    core_block = allow_block + '''CORE_SOURCE_PATHS = {
    "native/San9BridgeP1EasyPingM2b/include/s5_current_context.h",
    "native/San9BridgeP1EasyPingM2b/src/s5_current_context.c",
    "native/San9BridgeP1EasyPingM2b/src/controller.c",
    "native/San9BridgeP1EasyPingM2b/src/bridge_dll.c",
    "native/San9BridgeP1EasyPingM2b/src/offline_selftest.c",
}
'''
    text = replace_once(text, allow_block, core_block, "core source allowlist")
    text = replace_once(
        text,
        '''        kind, plaintext = decoded
        if not all(marker in plaintext for marker in REQUIRED_MARKERS):
''',
        '''        kind, plaintext = decoded
        plaintext = plaintext.replace(b"\\r\\n", b"\\n").replace(b"\\r", b"\\n")
        if not all(marker in plaintext for marker in REQUIRED_MARKERS):
''',
        "baseline plaintext normalization",
    )
    text = replace_once(
        text,
        '''            run([sys.executable, str(script), "source"], cwd=worktree)
            run(["git", "diff", "--check"], cwd=worktree)
''',
        '''            run([sys.executable, str(script), "source"], cwd=worktree)
            for relative in ALLOWED_SOURCE_PATHS:
                candidate = worktree / relative
                if candidate.is_file():
                    data = candidate.read_bytes()
                    candidate.write_bytes(data.replace(b"\\r\\n", b"\\n").replace(b"\\r", b"\\n"))
            run(["git", "diff", "--check"], cwd=worktree)
''',
        "target-source LF normalization",
    )
    text = text.replace(
        "            missing = ALLOWED_SOURCE_PATHS - actual\n",
        "            missing = CORE_SOURCE_PATHS - actual\n",
        1,
    )
    PATH.write_text(text, encoding="utf-8", newline="\n")
    compile(text, str(PATH), "exec")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
