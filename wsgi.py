# WSGI entry for Asset Tracking.
# PythonAnywhere: paste this into the Web tab WSGI file
#   /var/www/sujaykarmakar_pythonanywhere_com_wsgi.py
# Local production server: python wsgi.py
import sys
from pathlib import Path

pythonanywhere_home = Path("/home/sujaykarmakar/mysite")
local_home = Path(__file__).resolve().parent
project_home = str(pythonanywhere_home if pythonanywhere_home.is_dir() else local_home)

if project_home not in sys.path:
    sys.path.insert(0, project_home)

from app import app as application  # noqa: E402

if __name__ == "__main__":
    from waitress import serve

    serve(application, host="127.0.0.1", port=5000)
