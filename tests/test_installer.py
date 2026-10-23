import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import sys
import tempfile
import threading
import unittest
import zipfile
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from install_assets import download, safe_extract


class InstallerTests(unittest.TestCase):
    def test_download_resume_hash_and_repeat(self):
        data=b'actual verified bytes'*5000
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                offset=int(self.headers.get('Range','bytes=0-')[6:-1])
                self.send_response(206 if offset else 200)
                self.send_header('Content-Length',str(len(data)-offset));self.end_headers()
                self.wfile.write(data[offset:])
            def log_message(self,*_):pass
        server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
        thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        try:
            with tempfile.TemporaryDirectory(prefix='Install path with spaces ') as tmp:
                target=Path(tmp)/'artifact.bin'
                target.with_suffix('.bin.part').write_bytes(data[:12345])
                url=f'http://127.0.0.1:{server.server_port}/file'
                download(url,target,hashlib.sha256(data).hexdigest(),len(data))
                self.assertEqual(target.read_bytes(),data)
                stamp=target.stat().st_mtime_ns
                download(url,target,hashlib.sha256(data).hexdigest(),len(data))
                self.assertEqual(target.stat().st_mtime_ns,stamp)
        finally:
            server.shutdown();server.server_close();thread.join()

    def test_archive_traversal_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'bad.zip'
            with zipfile.ZipFile(p,'w') as z:z.writestr('../escape.txt','no')
            with self.assertRaises(ValueError):safe_extract(p,Path(tmp)/'dest')
            self.assertFalse((Path(tmp)/'escape.txt').exists())
