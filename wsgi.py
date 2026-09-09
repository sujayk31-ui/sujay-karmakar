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
    import os
    from waitress import serve

    port = int(os.environ.get("PORT", "5000"))
    serve(application, host="0.0.0.0", port=port)
