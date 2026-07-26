#!/usr/bin/env python3
"""用 Python 标准库驱动 .NET 6 TCP 长度前缀 Proto Spike。"""

from __future__ import annotations

import socket
import struct
import subprocess
import sys
from pathlib import Path


MAX_FRAME_LENGTH = 1_048_576
ROOT = Path(__file__).resolve().parent
PROJECT = ROOT / "TransportSpike.csproj"


def encode_varint(value: int) -> bytes:
    encoded = bytearray()
    while value > 0x7F:
        encoded.append((value & 0x7F) | 0x80)
        value >>= 7
    encoded.append(value)
    return bytes(encoded)


def encode_ping_transport_frame(message_id: str, sequence: int) -> bytes:
    """编码 TransportFrame.message_id + TransportFrame.ping。"""
    message_id_bytes = message_id.encode("ascii")
    ping = b"\x08" + encode_varint(sequence)
    return (
        b"\x0a"
        + encode_varint(len(message_id_bytes))
        + message_id_bytes
        + encode_varint((30 << 3) | 2)
        + encode_varint(len(ping))
        + ping
    )


def frame(payload: bytes) -> bytes:
    if not 1 <= len(payload) <= MAX_FRAME_LENGTH:
        raise ValueError("payload length outside V1 frame boundary")
    return struct.pack(">I", len(payload)) + payload


def recv_exact(sock: socket.socket, length: int) -> bytes:
    chunks = bytearray()
    while len(chunks) < length:
        chunk = sock.recv(length - len(chunks))
        if not chunk:
            raise EOFError(f"expected {length} bytes, received {len(chunks)}")
        chunks.extend(chunk)
    return bytes(chunks)


def recv_frame(sock: socket.socket) -> bytes:
    length = struct.unpack(">I", recv_exact(sock, 4))[0]
    if not 1 <= length <= MAX_FRAME_LENGTH:
        raise ValueError(f"invalid frame length: {length}")
    return recv_exact(sock, length)


def connect(port: int) -> socket.socket:
    sock = socket.create_connection(("127.0.0.1", port), timeout=5)
    sock.settimeout(5)
    return sock


def run_cases(port: int) -> None:
    short_read = encode_ping_transport_frame("short-read", 1)
    sticky_a = encode_ping_transport_frame("sticky-a", 2)
    sticky_b = encode_ping_transport_frame("sticky-b", 3)

    with connect(port) as sock:
        sock.sendall(frame(short_read))
        if recv_frame(sock) != short_read:
            raise AssertionError("short-read round trip changed Proto payload")

        # 两个完整帧在一次 sendall 中写入，验证读取器不把它们合并为一个帧。
        sock.sendall(frame(sticky_a) + frame(sticky_b))
        if recv_frame(sock) != sticky_a or recv_frame(sock) != sticky_b:
            raise AssertionError("sticky-frame boundary was not preserved")
        sock.shutdown(socket.SHUT_WR)

    with connect(port) as sock:
        sock.sendall(struct.pack(">I", 0))
        sock.shutdown(socket.SHUT_WR)
        if sock.recv(1) != b"":
            raise AssertionError("zero-length frame did not fail closed")

    with connect(port) as sock:
        sock.sendall(struct.pack(">I", MAX_FRAME_LENGTH + 1))
        sock.shutdown(socket.SHUT_WR)
        if sock.recv(1) != b"":
            raise AssertionError("oversized frame did not fail closed")

    with connect(port) as sock:
        sock.sendall(b"\x00\x00")
        sock.shutdown(socket.SHUT_WR)
        if sock.recv(1) != b"":
            raise AssertionError("short header did not fail closed")

    with connect(port) as sock:
        sock.sendall(struct.pack(">I", 8) + b"\x0a\x01x")
        sock.shutdown(socket.SHUT_WR)
        if sock.recv(1) != b"":
            raise AssertionError("short payload did not fail closed")


def main() -> int:
    command = ["dotnet", "run", "--project", str(PROJECT), "--nologo"]
    process = subprocess.Popen(
        command,
        cwd=ROOT,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
    )
    if process.stdout is None:
        process.kill()
        raise RuntimeError("failed to capture dotnet listener stdout")

    startup_lines: list[str] = []
    try:
        while True:
            line = process.stdout.readline()
            if line == "":
                stderr = process.stderr.read() if process.stderr is not None else ""
                raise RuntimeError(
                    "dotnet listener exited before READY\n"
                    + "".join(startup_lines)
                    + stderr
                )
            startup_lines.append(line)
            if line.startswith("READY "):
                port = int(line.split()[1])
                break

        run_cases(port)
        stdout_tail, stderr = process.communicate(timeout=15)
        complete_stdout = "".join(startup_lines) + stdout_tail
        if process.returncode != 0:
            raise RuntimeError(
                f"dotnet listener exited with {process.returncode}\n"
                + complete_stdout
                + stderr
            )
        if "SPIKE_OK cases=5" not in complete_stdout:
            raise AssertionError("listener did not report all five passing cases")

        print(complete_stdout.strip())
        print("PYTHON_OK protobuf_wire=true framed_roundtrip=3 negative_cases=4")
        return 0
    except Exception:
        process.kill()
        process.wait(timeout=5)
        raise


if __name__ == "__main__":
    sys.exit(main())
