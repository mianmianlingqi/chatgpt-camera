"""Local HTTPS integration test; never touches the user's Codex interface."""
import hashlib, http.client, io, json, pathlib, ssl, subprocess, sys, tempfile, time, uuid
from PIL import Image

exe = pathlib.Path(sys.argv[1]).resolve()
assert exe.is_file(), exe
directory = pathlib.Path(tempfile.mkdtemp(prefix='camera-test-'))
passed = 0
def check(name, value):
    global passed
    assert value, name
    passed += 1
    print('PASS', name, flush=True)
def start():
    ready = directory / 'test-ready.json'
    if ready.exists(): ready.unlink()
    process = subprocess.Popen([str(exe), '--test-server', str(directory)], creationflags=subprocess.CREATE_NO_WINDOW)
    for _ in range(100):
        if ready.exists(): return process, json.loads(ready.read_text())
        if process.poll() is not None: raise RuntimeError('Server exited: ' + str(process.returncode))
        time.sleep(.1)
    process.terminate(); raise TimeoutError('Server startup')
process, config = start()
def request(method, path, body=None, auth=True, content='image/jpeg'):
    conn = http.client.HTTPSConnection('127.0.0.1', config['Port'], context=ssl._create_unverified_context(), timeout=20)
    conn.connect()
    assert hashlib.sha256(conn.sock.getpeercert(binary_form=True)).hexdigest() == config['Pin']
    headers = {'Content-Type': content}
    if auth: headers['Authorization'] = 'Bearer ' + config['Token']
    try:
        conn.request(method, path, body, headers)
        res = conn.getresponse(); status = res.status; raw = res.read()
        return status, json.loads(raw) if raw else None
    finally: conn.close()
try:
    check('unauthenticated request rejected', request('GET', '/v1/health', auth=False)[0] == 401)
    check('authenticated TLS health', request('GET', '/v1/health')[0] == 200)
    capture = uuid.uuid4().hex; url = '/v1/captures/' + capture
    check('reserve capture', request('POST', url)[1]['state'] == 'reserved')
    check('reject invalid image', request('PUT', url + '/image', b'not a photo')[0] == 400)
    check('reject wrong MIME', request('PUT', url + '/image', b'abc', content='text/plain')[0] == 415)
    data = io.BytesIO(); Image.new('RGB', (64, 64), (35, 150, 120)).save(data, 'JPEG'); jpeg = data.getvalue()
    check('store valid JPEG', request('PUT', url + '/image', jpeg)[1]['stored'])
    check('idempotent retry', request('PUT', url + '/image', jpeg)[0] == 200)
    data = io.BytesIO(); Image.new('RGB', (32, 32), (120, 45, 5)).save(data, 'JPEG')
    check('reject conflicting retry', request('PUT', url + '/image', data.getvalue())[0] == 409)
    check('duplicate reserve retains status', request('POST', url)[1]['stored'])
    check('bytes preserved', (directory / 'photos' / (capture + '.jpg')).read_bytes() == jpeg)
    if len(sys.argv) > 2:
        props = directory / 'java.properties'
        props.write_text('port=' + str(config['Port']) + '\ntoken=' + config['Token'] + '\npin=' + config['Pin'] + '\njpeg=' + (directory / 'photos' / (capture + '.jpg')).as_posix() + '\n')
        subprocess.run(['java', '-cp', sys.argv[2], 'local.chatgpt.camera.TransportTest', str(props)], check=True)
    check('unknown ID cannot upload', request('PUT', '/v1/captures/' + uuid.uuid4().hex + '/image', jpeg)[0] == 404)
    check('invalid ID rejected', request('POST', '/v1/captures/invalid')[0] == 400)
    process.terminate(); process.wait(timeout=10)
    process, config = start()
    result = request('GET', url)[1]
    check('restart retains photo and holds attachment', result['stored'] and result['state'] == 'held')
    print(str(passed) + ' integration checks passed')
finally:
    process.terminate(); process.wait(timeout=10)
    # Test directory intentionally retained for reproducible evidence; contains synthetic photos only.
    print('Evidence directory:', directory)
