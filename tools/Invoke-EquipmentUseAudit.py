"""Run functional equipment tests against the isolated verification server."""
import pathlib
import re
import socket
import time

output = pathlib.Path(__file__).resolve().parents[1] / '.local-tests/use-audit.txt'
chunks = []
with socket.create_connection(('127.0.0.1', 26939), timeout=5) as connection:
    connection.settimeout(.5)
    connection.sendall(b'aecusecheck\r\n')
    deadline = time.monotonic() + 55
    while time.monotonic() < deadline:
        try:
            data = connection.recv(65536)
            if not data:
                break
            chunks.append(data)
            result = b''.join(chunks).decode('utf-8', errors='replace')
            if re.search(r'\[AEC-Use-Audit\] (?:PASS|FAIL) checks=\d+;[^\r\n]*\r?\n', result):
                break
        except socket.timeout:
            continue
    connection.sendall(b'exit\r\n')
    end = time.monotonic() + 3
    while time.monotonic() < end:
        try:
            if not connection.recv(65536):
                break
        except socket.timeout:
            continue
result = b''.join(chunks).decode('utf-8', errors='replace')
output.write_text(result, encoding='utf-8')
lines = [line.strip() for line in result.splitlines() if '[AEC-Use-Audit]' in line]
print('\n'.join(lines))
assert re.search(r'\[AEC-Use-Audit\] PASS checks=\d+; failures=0\.', result), 'See ' + str(output)
