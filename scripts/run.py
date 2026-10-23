"""Absolute-root entry point for the private Python; independent of current directory."""
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from yumi.__main__ import main
raise SystemExit(main())
