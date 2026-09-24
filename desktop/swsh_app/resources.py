import os
from pathlib import Path


def resource_path(*parts):
    root = Path(__file__).resolve().parents[2]
    if parts and parts[0] == "easycon_native":
        configured = os.environ.get("SWSH_TESSERACT_ROOT")
        if configured:
            return Path(configured).joinpath(*parts[1:])
        return root / "desktop" / "easycon_native"
    return root.joinpath(*parts)
