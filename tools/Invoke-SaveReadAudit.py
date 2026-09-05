"""Run the native save regression on the isolated verification server."""
import pathlib
import re
import socket
import time

output = pathlib.Path(__file__).resolve().parents[1] / '.local-tests/save-read-audit.txt'
chunks = []
with socket.create_connection(('127.0.0.1', 26939), timeout=5) as connection:
    connection.settimeout(.5)
    connection.sendall(b'aecsavecheck\r\n')
    deadline = time.monotonic() + 55
    while time.monotonic() < deadline:
        try:
            data = connection.recv(65536)
            if not data:
                break
            chunks.append(data)
            result = b''.join(chunks).decode('utf-8', errors='replace')
            if re.search(r'\[AEC-Save-Audit\] (?:PASS|FAIL) checks=\d+;[^\r\n]*\r?\n', result):
                break
        except socket.timeout:
            continue
    connection.sendall(b'exit\r\n')
result = b''.join(chunks).decode('utf-8', errors='replace')
output.write_text(result, encoding='utf-8')
print('\n'.join(line.strip() for line in result.splitlines() if '[AEC-Save-Audit]' in line))
assert re.search(r'\[AEC-Save-Audit\] PASS checks=\d+; failures=0\.', result), 'See ' + str(output)
