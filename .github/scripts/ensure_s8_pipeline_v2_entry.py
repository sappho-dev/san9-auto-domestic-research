from __future__ import annotations

from pathlib import Path

PATH = Path(__file__).with_name("run_s8_candidate_pipeline.ps1")


def main() -> int:
    text = PATH.read_text(encoding="utf-8")
    text = text.replace(
        "python .github/scripts/apply_s8_function_patch.py\n",
        "python .github/scripts/apply_s8_function_patch_v2.py\n",
        1,
    )
    marker = "    .github/scripts/apply_s8_function_patch.py `\n"
    addition = marker + "    .github/scripts/apply_s8_function_patch_v2.py `\n"
    if ".github/scripts/apply_s8_function_patch_v2.py" not in text.split("python -m py_compile", 1)[-1].split("git diff --check", 1)[0]:
        if marker not in text:
            raise RuntimeError("Cannot add v2 source entrypoint to py_compile list")
        text = text.replace(marker, addition, 1)
    PATH.write_text(text, encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
