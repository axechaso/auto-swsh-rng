"""Launch the Sword / Shield desktop app, with an isolated screenshot mode."""
import argparse
import sys
from pathlib import Path

from PySide6.QtCore import QStandardPaths, QTimer
from PySide6.QtGui import QFont
from PySide6.QtWidgets import QApplication

from swsh_app.backend import find_backend
from swsh_app.window import PAGES, SwshWindow


def main(argv=None):
    parser = argparse.ArgumentParser(description="剑 / 盾乱数工具 · PySide6")
    parser.add_argument("--backend", help="AutoSwshRng.Cli.exe 路径")
    parser.add_argument("--data-dir", type=Path)
    parser.add_argument("--size", default="1440x900")
    parser.add_argument("--page", choices=[p[0] for p in PAGES], default="search")
    parser.add_argument("--screenshot", type=Path)
    parser.add_argument("--no-backend", action="store_true", help="仅查看界面，不自动读取遭遇表")
    args = parser.parse_args(argv)
    app = QApplication(sys.argv[:1])
    app.setApplicationName("SwshRngDesktop")
    app.setOrganizationName("AutoSwshRng")
    app.setStyle("Fusion")
    app.setFont(QFont("Microsoft YaHei UI", 9))
    data_dir = args.data_dir or Path(QStandardPaths.writableLocation(QStandardPaths.StandardLocation.AppLocalDataLocation))
    window = SwshWindow(data_dir, Path(args.backend).resolve() if args.backend else None, auto_load=not args.no_backend)
    width, height = map(int, args.size.lower().split("x"))
    window.resize(width, height)
    window.select_page(args.page)
    window.show()
    if args.screenshot:
        def capture():
            if window.jobs:
                QTimer.singleShot(100, capture)
                return
            args.screenshot.parent.mkdir(parents=True, exist_ok=True)
            saved = window.grab().save(str(args.screenshot))
            window.close()
            app.exit(0 if saved else 2)
        QTimer.singleShot(600, capture)
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
