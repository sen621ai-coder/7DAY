"""Run the read-only alarm audit on the local isolated verification server."""
from pathlib import Path
import re
import socket
import time

output = Path(__file__).resolve().parents[1] / '.local-tests/alarm-audit.txt'
chunks = []
with socket.create_connection(('127.0.0.1', 26939), timeout=5) as connection:
    connection.settimeout(.5)
    connection.sendall(b'logicAlarmCheck\r\n')
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        try:
            data = connection.recv(65536)
            if not data:
                break
            chunks.append(data)
            result = b''.join(chunks).decode('utf-8', errors='replace')
            if re.search(r'\[LogicAlarm-Audit\] (?:PASS|FAIL)[^\r\n]*\r?\n', result):
                break
        except socket.timeout:
            continue
    connection.sendall(b'exit\r\n')
result = b''.join(chunks).decode('utf-8', errors='replace')
output.write_text(result, encoding='utf-8')
print('\n'.join(line for line in result.splitlines() if '[LogicAlarm-Audit]' in line))
assert re.search(r'\[LogicAlarm-Audit\] PASS checks=\d+; failures=0\.', result), 'See ' + str(output)
