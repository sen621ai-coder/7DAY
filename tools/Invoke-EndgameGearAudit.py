"""Run the read-only native gear check on the isolated verification server."""
import pathlib
import re
import socket
import time

output = pathlib.Path(__file__).resolve().parents[1] / '.local-tests/gear-audit.txt'
chunks = []
with socket.create_connection(('127.0.0.1', 26939), timeout=5) as connection:
    connection.settimeout(.5)
    connection.sendall(b'aecgearcheck\r\n')
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        try:
            data = connection.recv(65536)
            if not data:
                break
            chunks.append(data)
            text = b''.join(chunks).decode('utf-8', errors='replace')
            if re.search(r'\[AEC-Gear-Audit\] (?:PASS|FAIL) 92 tier/family checks; failures=\d+\.[^\r\n]*\r?\n', text):
                break
        except socket.timeout:
            continue
    # Ask the server to close the connection, avoiding a client-side reset while
    # the server is still streaming its startup log to connected Telnet clients.
    connection.sendall(b'exit\r\n')
    end = time.monotonic() + 3
    while time.monotonic() < end:
        try:
            if not connection.recv(65536):
                break
        except socket.timeout:
            continue
text = b''.join(chunks).decode('utf-8', errors='replace')
lines = [line.strip() for line in text.splitlines() if '[AEC-Gear-Audit]' in line]
output.write_text('\n'.join(lines) + '\n', encoding='utf-8')
assert '[AEC-Gear-Audit] PASS 92 tier/family checks; failures=0.' in text and '[AEC-Gear-Audit] FAIL' not in text, output.read_text(encoding='utf-8')
print(lines[-1])
print('Evidence: ' + str(output))
