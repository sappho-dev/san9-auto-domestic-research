from __future__ import annotations

from pathlib import Path

PATH = Path(__file__).with_name("materialize_s8_function_patch_v3.py")


def main() -> int:
    text = PATH.read_text(encoding="utf-8")
    before = '''        run(
            ["git", "worktree", "add", "--detach", str(worktree), "HEAD"],
            cwd=root,
        )
'''
    after = '''        run(
            ["git", "worktree", "add", "--detach", str(worktree), BASELINE],
            cwd=root,
        )
'''
    if after not in text:
        if before not in text:
            raise RuntimeError("Cannot pin S8 source patch generation to baseline tree")
        text = text.replace(before, after, 1)
    PATH.write_text(text, encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
