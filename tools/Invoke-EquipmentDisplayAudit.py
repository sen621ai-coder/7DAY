"""Read-only equipment display check on the isolated native server."""
import pathlib
import re
import socket
import time

output = pathlib.Path(__file__).resolve().parents[1] / '.local-tests/display-audit.txt'
log = output.parent / 'server.log'
assert log.exists() and 'StartGame done' in log.read_text(encoding='utf-8', errors='replace'), 'Wait for the isolated server to finish startup before running the UI audit.'
chunks = []
with socket.create_connection(('127.0.0.1', 26939), timeout=5) as connection:
    connection.settimeout(.5)
    connection.sendall(b'aecequipmentdisplaycheck\r\n')
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        try:
            data = connection.recv(65536)
            if not data:
                break
            chunks.append(data)
            result = b''.join(chunks).decode('utf-8', errors='replace')
            if re.search(r'\[AEC-Display-Audit\] (?:PASS|FAIL) checks=\d+;[^\r\n]*\r?\n', result):
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
lines = [line.strip() for line in result.splitlines() if '[AEC-Display-Audit]' in line]
output.write_text('\n'.join(lines) + '\n', encoding='utf-8')
assert re.search(r'\[AEC-Display-Audit\] PASS checks=\d+; failures=0', result) and '[AEC-Display-Audit] FAIL' not in result, output.read_text(encoding='utf-8')
print(lines[-1])
print('Evidence: ' + str(output))
