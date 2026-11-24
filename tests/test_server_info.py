"""GET /api/info lets a client tell a 哗啦待办 server apart before signing in:
public, no account data, the same answer for everyone."""
import json
import socket
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.request
from pathlib import Path

from server import SERVER_INFO, Store


class ServerInfoTests(unittest.TestCase):
    def test_it_names_the_product_and_an_integer_api_version_only(self):
        self.assertEqual(SERVER_INFO['product'], 'hola-calendar')
        self.assertIsInstance(SERVER_INFO['api'], int)
        self.assertEqual(set(SERVER_INFO), {'product', 'name', 'api'}, 'nothing about the team or its accounts')


class ServerInfoHTTPTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        db = Path(cls.tmp.name) / 'info.sqlite3'
        store = Store(db, seed=False)
        store.create_user('superadmin', 'password-long', True)
        store.db.commit(); store.close()
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0)); port = sock.getsockname()[1]
        cls.base = f'http://127.0.0.1:{port}'
        cls.open = urllib.request.build_opener(urllib.request.ProxyHandler({})).open
        cls.process = subprocess.Popen([sys.executable, str(Path(__file__).resolve().parents[1] / 'server.py'),
                                        '--port', str(port), '--db', str(db)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for _ in range(100):
            try:
                with cls.open(cls.base + '/api/health', timeout=.2):
                    return
            except OSError:
                if cls.process.poll() is not None:
                    break
                time.sleep(.05)
        cls.process.terminate(); cls.process.wait(timeout=5); cls.tmp.cleanup()
        raise RuntimeError('isolated HTTP test server did not start')

    @classmethod
    def tearDownClass(cls):
        cls.process.terminate(); cls.process.wait(timeout=5); cls.tmp.cleanup()

    def test_anyone_can_read_it_without_signing_in(self):
        with self.open(self.base + '/api/info', timeout=5) as response:
            self.assertEqual(response.status, 200)
            self.assertEqual(response.headers['Cache-Control'], 'no-store')
            self.assertEqual(json.loads(response.read()), SERVER_INFO)


if __name__ == '__main__':
    unittest.main()
