#!/usr/bin/env python3
"""Exercise actual MCP stdio JSON-RPC, not only compilation.
Requires a previously built Migrator.Mcp.dll and Python 3 on Linux.
"""
import json
import os
import pathlib
import select
import subprocess
import sys
import tempfile
import time


def expect(condition, message):
    if not condition:
        raise AssertionError(message)


def receive(proc, message_id, timeout=30):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        readable, _, _ = select.select([proc.stdout], [], [], max(0, deadline - time.monotonic()))
        if not readable:
            break
        line = proc.stdout.readline()
        if not line:
            raise AssertionError("MCP process terminated before response (exit=%s)" % proc.poll())
        try:
            payload = json.loads(line)
        except json.JSONDecodeError as ex:
            raise AssertionError("Non-JSON output on MCP stdout: " + repr(line[:500])) from ex
        if payload.get("id") == message_id:
            expect("error" not in payload, f"JSON-RPC error for {message_id}: {payload}")
            return payload["result"]
    raise TimeoutError(f"Timed out waiting for MCP response {message_id}")


def send(proc, payload):
    proc.stdin.write((json.dumps(payload, separators=(",", ":")) + "\n").encode("utf-8"))
    proc.stdin.flush()


def call(proc, message_id, name, arguments):
    send(proc, {"jsonrpc": "2.0", "id": message_id, "method": "tools/call",
                "params": {"name": name, "arguments": arguments}})
    return receive(proc, message_id)


def value(result):
    expect(not result.get("isError", False), f"Tool error: {result}")
    content = result.get("content", [])
    expect(content and content[0]["type"] == "text", f"Text result missing: {result}")
    return json.loads(content[0]["text"])


def main():
    if len(sys.argv) != 2:
        raise SystemExit("Usage: mcp_stdio_smoke.py <compiled Migrator.Mcp.dll>")
    dll = str(pathlib.Path(sys.argv[1]).resolve())
    expect(pathlib.Path(dll).exists(), "MCP dll does not exist: " + dll)
    with tempfile.TemporaryDirectory(prefix="mcp-stdio-smoke-") as root:
        product = pathlib.Path(root, "product")
        product.mkdir()
        sample = product / "Tests.cs"
        sample.write_text("""
using NUnit.Framework;
[TestFixture] public class AccountTests
{
    [Test] public void OpensPage() {}
}
""", encoding="utf-8")
        env = dict(os.environ, MIGRATOR_WORKSPACE=str(product))
        proc = subprocess.Popen(["dotnet", dll], stdin=subprocess.PIPE,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
        try:
            send(proc, {"jsonrpc": "2.0", "id": 1, "method": "initialize",
                        "params": {"protocolVersion": "2025-06-18", "capabilities": {},
                                   "clientInfo": {"name": "migrator-smoke", "version": "0.1"}}})
            init = receive(proc, 1)
            expect("serverInfo" in init, f"Initialize missing serverInfo: {init}")
            send(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})
            send(proc, {"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}})
            listing = receive(proc, 2)
            names = {tool["name"] for tool in listing.get("tools", [])}
            expect({"analyze_migration", "preview_migration"} <= names,
                   f"Missing MCP tools: {names}")
            report = value(call(proc, 3, "analyze_migration", {"source": "."}))
            expect(report["discoveryStatus"] == "complete", f"Incorrect discovery: {report}")
            expect(report["declaredTests"] == 1 and report["parsedTests"] == 1,
                   f"Incorrect test count: {report}")
            expected_id = report["analysisId"]
            preview = value(call(proc, 4, "preview_migration",
                                 {"source": ".", "expectedAnalysisId": expected_id}))
            expect(len(preview["files"]) == 1, f"Missing generated file: {preview}")
            expect(len(preview["targetTreeHash"]) == 64, "Target hash missing")
            expect(preview["analysisId"] == expected_id, "Preview analysis mismatch")
            outside = call(proc, 5, "analyze_migration", {"source": "../"})
            expect(outside.get("isError"), "Parent directory traversal was accepted")
            absolute = call(proc, 6, "analyze_migration", {"source": str(product)})
            expect(absolute.get("isError"), "Absolute source path was accepted")
            sample.write_text(sample.read_text(encoding="utf-8") + "\n// changed\n",
                              encoding="utf-8")
            stale = call(proc, 7, "preview_migration",
                         {"source": ".", "expectedAnalysisId": expected_id})
            expect(stale.get("isError"), "Stale analysis ID was accepted")
            sample.unlink()
            sample.write_text("""
using NUnit.Framework;
[TestFixture] public class First { [Test] public void A() {} }
[TestFixture] public class Second { [Test] public void B() {} }
""", encoding="utf-8")
            multiple = value(call(proc, 8, "analyze_migration", {"source": "."}))
            expect(multiple["discoveryStatus"] == "partial"
                   and multiple["declaredTests"] == 2, "Multi-fixture source was hidden")
            print("MCP stdio integration smoke passed", flush=True)
        finally:
            proc.terminate()
            try:
                proc.communicate(timeout=10)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.communicate(timeout=10)


if __name__ == "__main__":
    main()
