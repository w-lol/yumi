from __future__ import annotations
import argparse
import os
from pathlib import Path
import sys
from .paths import Paths
from .settings import Settings


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument('--smoke', action='store_true')
    args = parser.parse_args()
    paths = Paths(args.root)
    paths.activate()
    if args.smoke:
        from .smoke import main as smoke
        return smoke(paths.root)
    from PySide6.QtCore import QLockFile
    from PySide6.QtWidgets import QApplication, QMessageBox
    app = QApplication(sys.argv[:1])
    app.setApplicationName('Yumi')
    app.setQuitOnLastWindowClosed(False)
    lock = QLockFile(str(paths.tmp / 'yumi.lock'))
    lock.setStaleLockTime(0)
    if not lock.tryLock(100):
        QMessageBox.information(None, 'Yumi is already open', 'Use the existing Yumi Control Board. A second instance will not take over the devices or database.')
        return 1
    from .ui import Board
    board = Board(paths, Settings(paths))
    board.show()
    app.aboutToQuit.connect(lock.unlock)
    result = app.exec()
    return result


if __name__ == '__main__':
    raise SystemExit(main())
