"""Read-only weapon function check on the isolated native server."""
import pathlib
import re
import socket
import time

output = pathlib.Path(__file__).resolve().parents[1] / '.local-tests/weapon-audit.txt'
chunks = []
with socket.create_connection(('127.0.0.1', 26939), timeout=5) as connection:
    connection.settimeout(.5)
    connection.sendall(b'aecweaponcheck\r\n')
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        try:
            data = connection.recv(65536)
            if not data:
                break
            chunks.append(data)
            result = b''.join(chunks).decode('utf-8', errors='replace')
            if re.search(r'\[AEC-Weapon-Audit\] (?:PASS|FAIL) checks=\d+;[^\r\n]*\r?\n', result):
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
lines = [line.strip() for line in result.splitlines() if '[AEC-Weapon-Audit]' in line]
output.write_text('\n'.join(lines) + '\n', encoding='utf-8')
assert re.search(r'\[AEC-Weapon-Audit\] PASS checks=\d+; failures=0\.', result) and '[AEC-Weapon-Audit] FAIL' not in result, output.read_text(encoding='utf-8')
print(lines[-1])
print('Evidence: ' + str(output))
