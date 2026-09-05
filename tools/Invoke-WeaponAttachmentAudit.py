"""Run the native attachment regression in the isolated verification server."""
import pathlib
import re
import socket
import time

output = pathlib.Path(__file__).resolve().parents[1] / '.local-tests/attachment-audit.txt'
chunks = []
with socket.create_connection(('127.0.0.1', 26939), timeout=5) as connection:
    connection.settimeout(.5)
    connection.sendall(b'aecattachmentcheck\r\n')
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        try:
            data = connection.recv(65536)
            if not data:
                break
            chunks.append(data)
            result = b''.join(chunks).decode('utf-8', errors='replace')
            if re.search(r'\[AEC-Attachment-Audit\] (?:PASS|FAIL) weapons=\d+; checks=\d+; failures=\d+\r?\n', result):
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
lines = [line.strip() for line in result.splitlines() if '[AEC-Attachment-Audit]' in line]
output.write_text('\n'.join(lines) + '\n', encoding='utf-8')
assert re.search(r'\[AEC-Attachment-Audit\] PASS weapons=28; checks=\d+; failures=0', result) and '[AEC-Attachment-Audit] FAIL' not in result, result
print(lines[-1])
print('Evidence: ' + str(output))
