"""Package only the final selected voices; never include unused training weights."""
from pathlib import Path
import subprocess
import sys

if __name__ == '__main__':
    script = Path(__file__).resolve().parents[2] / 'tools/archive_voice.py'
    subprocess.run([sys.executable, str(script), '--models-only', *sys.argv[1:]], check=True)
