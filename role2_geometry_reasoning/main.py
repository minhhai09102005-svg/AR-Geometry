
"""SmartGeoAR Role 2: demo va local coordinator cho Role 1 / Role 3."""

import argparse
import json
import os
import socket
import sys
import threading
import time
from dataclasses import asdict

from reasoning_engine import GeometryReasoningEngine
from schemas import CVDataInput, VertexInput


HOST = "127.0.0.1"
UNITY_TCP_PORT = 5053
ROLE1_UDP_PORT = 5052

SHAPES = {
    "cube",
    "rectangular_prism",
    "square_pyramid"
}

clients = set()
clients_lock = threading.Lock()
shutdown = threading.Event()


if sys.platform == "win32":
    os.system("")
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except (AttributeError, OSError):
        pass


class UnityClient:
    def __init__(self, connection):
        self.connection = connection
        self.write_lock = threading.Lock()

    def send(self, data):
        if isinstance(data, dict):
            raw = json.dumps(
                data,
                ensure_ascii=False,
                separators=(",", ":"),
            )
        else:
            raw = data

        payload = (raw.rstrip("\n") + "\n").encode("utf-8")

        try:
            with self.write_lock:
                self.connection.sendall(payload)
            return True
        except (OSError, ValueError):
            return False

    def close(self):
        try:
            self.connection.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass

        try:
            self.connection.close()
        except OSError:
            pass


def remove_client(client):
    with clients_lock:
        clients.discard(client)
    client.close()


def convert_graph_to_input(payload):
    shape = str(payload.get("target_shape", "")).strip()

    if shape not in SHAPES:
        raise ValueError(f"Unsupported target_shape: {shape}")

    raw_vertices = payload.get("vertices")
    raw_edges = payload.get("detected_edges")

    if not isinstance(raw_vertices, list):
        raise ValueError("vertices must be a list")

    if not isinstance(raw_edges, list):
        raise ValueError("detected_edges must be a list")

    if len(raw_vertices) > 100 or len(raw_edges) > 500:
        raise ValueError("Graph exceeds accepted size")

    vertices = []

    for vertex in raw_vertices:
        if not isinstance(vertex, dict):
            raise ValueError("Invalid vertex")

        vertex_id = str(vertex["id"])

        if not vertex_id:
            raise ValueError("Empty vertex id")

        vertices.append(
            VertexInput(
                id=vertex_id,
                x=float(vertex.get("x", 0.0)),
                y=float(vertex.get("y", 0.0)),
                z=float(vertex.get("z", 0.0)),
                confidence=float(
                    vertex.get("confidence", 1.0)
                ),
            )
        )

    edges = []

    for edge in raw_edges:
        if not isinstance(edge, list) or len(edge) != 2:
            raise ValueError("Invalid detected edge")

        edges.append(
            (str(edge[0]), str(edge[1]))
        )

    cv_input = CVDataInput(
        timestamp=float(payload.get("timestamp", time.time())),
        frame_id=int(payload.get("frame_id", 0)),
        target_shape=shape,
        vertices=vertices,
        detected_edges=edges,
    )

    return shape, cv_input


def evaluate_graph(payload):
    shape, cv_input = convert_graph_to_input(payload)

    engine = GeometryReasoningEngine(
        target_shape=shape,
        auto_detect_shape=False,
        enable_3d_validation=True,
        enable_smoothing=False,
    )

    result = engine.process(cv_input)

    response = asdict(result)

    response["source"] = "role2_geometry_reasoning"
    response["session_id"] = payload.get("session_id", "")
    response["frame_id"] = cv_input.frame_id
    response["vertex_count"] = len(cv_input.vertices)
    response["edge_count"] = len(cv_input.detected_edges)

    return response


def handle_unity(client, address):
    print(f"[UNITY] Connected: {address}", flush=True)

    with clients_lock:
        clients.add(client)

    try:
        with client.connection.makefile(
            "r",
            encoding="utf-8",
            newline="\n",
        ) as reader:
            for line in reader:
                if shutdown.is_set():
                    break

                if len(line) > 100000:
                    raise ValueError("Input line is too large")

                try:
                    payload = json.loads(line)
                    if not isinstance(payload, dict):
                        raise ValueError("Expected JSON object")

                    if payload.get("source") != "role3_unity":
                        continue

                    if payload.get("type") == "ping":
                        client.send({
                            "source": "role2_geometry_reasoning",
                            "type": "pong"
                        })
                        continue

                    result = evaluate_graph(payload)

                    if result["status"] == "valid":
                        print(
                            "[ROLE 2] VALID "
                            f"{result['shape']} "
                            f"frame={result['frame_id']}",
                            flush=True
                        )
                    else:
                        print(
                            "[ROLE 2] "
                            f"{result['status']} "
                            f"frame={result['frame_id']} "
                            f"edges={result['edge_count']}",
                            flush=True
                        )

                    if not client.send(result):
                        break

                except (ValueError, TypeError, KeyError) as error:
                    message = {
                        "source": "role2_geometry_reasoning",
                        "type": "error",
                        "message": str(error)
                    }
                    if not client.send(message):
                        break

    except (OSError, UnicodeError, ValueError) as error:
        if not shutdown.is_set():
            print(f"[UNITY] Disconnected: {error}", flush=True)

    finally:
        remove_client(client)
        print("[UNITY] Connection closed", flush=True)


def role1_udp_loop():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
        udp.bind((HOST, ROLE1_UDP_PORT))
        udp.settimeout(0.5)

        print(
            f"[ROLE 1] Listening UDP {HOST}:{ROLE1_UDP_PORT}",
            flush=True
        )

        while not shutdown.is_set():
            try:
                raw, _ = udp.recvfrom(65535)
            except socket.timeout:
                continue
            except OSError:
                break

            try:
                packet = json.loads(raw.decode("utf-8"))

                if not isinstance(packet, dict):
                    continue

                if packet.get("source") != "role1_hand_tracking":
                    continue

                if packet.get("coordinate_system") != "normalized_image":
                    continue

                if not isinstance(packet.get("hands"), list):
                    continue

                # Giữ nguyên packet Role 1, không đổi tên field.
                encoded = json.dumps(
                    packet,
                    ensure_ascii=False,
                    separators=(",", ":")
                )

            except (ValueError, UnicodeError):
                continue

            with clients_lock:
                current_clients = list(clients)

            for client in current_clients:
                if not client.send(encoded):
                    remove_client(client)


def serve():
    print("=" * 58)
    print("SMARTGEOAR - LOCAL ROLE 1 / ROLE 2 / ROLE 3")
    print("=" * 58)
    print("Role 1 UDP : 127.0.0.1:5052")
    print("Unity TCP  : 127.0.0.1:5053")
    print("Stop       : Ctrl+C")
    print("=" * 58, flush=True)

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
        server.setsockopt(
            socket.SOL_SOCKET,
            socket.SO_REUSEADDR,
            1
        )
        server.bind((HOST, UNITY_TCP_PORT))
        server.listen(5)
        server.settimeout(0.5)

        udp_thread = threading.Thread(
            target=role1_udp_loop,
            daemon=True
        )
        udp_thread.start()

        try:
            while not shutdown.is_set():
                try:
                    connection, address = server.accept()
                except socket.timeout:
                    continue

                connection.setsockopt(
                    socket.IPPROTO_TCP,
                    socket.TCP_NODELAY,
                    1
                )

                client = UnityClient(connection)

                threading.Thread(
                    target=handle_unity,
                    args=(client, address),
                    daemon=True
                ).start()

        except KeyboardInterrupt:
            print("\nStopping SmartGeoAR...", flush=True)

        finally:
            shutdown.set()

            with clients_lock:
                current_clients = list(clients)

            for client in current_clients:
                remove_client(client)


def run_demo():
    scenarios = [
        (
            "Cube: missing edges",
            "cube",
            list("ABCDEFGH"),
            [("A", "B"), ("B", "C"), ("C", "D"), ("D", "A")]
        ),
        (
            "Cube: wrong connection",
            "cube",
            list("ABCDEFGH"),
            [
                ("A", "B"), ("B", "C"), ("C", "D"),
                ("D", "A"), ("A", "C")
            ]
        ),
        (
            "Cube: complete",
            "cube",
            list("ABCDEFGH"),
            [
                ("A", "B"), ("B", "C"), ("C", "D"), ("D", "A"),
                ("E", "F"), ("F", "G"), ("G", "H"), ("H", "E"),
                ("A", "E"), ("B", "F"), ("C", "G"), ("D", "H")
            ]
        ),
        (
            "Cube: missing vertices",
            "cube",
            list("ABCD"),
            [("A", "B"), ("B", "C"), ("C", "D"), ("D", "A")]
        ),
        (
            "Square pyramid: complete",
            "square_pyramid",
            ["A", "B", "C", "D", "S"],
            [
                ("A", "B"), ("B", "C"), ("C", "D"), ("D", "A"),
                ("S", "A"), ("S", "B"), ("S", "C"), ("S", "D")
            ]
        ),
    ]

    print("SmartGeoAR Role 2 demo")

    for title, shape, vertex_ids, edges in scenarios:
        payload = {
            "source": "role3_unity",
            "session_id": "demo",
            "frame_id": 1,
            "timestamp": time.time(),
            "target_shape": shape,
            "vertices": [
                {
                    "id": vertex_id,
                    "x": 0.0,
                    "y": 0.0,
                    "z": 0.0
                }
                for vertex_id in vertex_ids
            ],
            "detected_edges": [
                list(edge) for edge in edges
            ]
        }

        result = evaluate_graph(payload)

        print(
            f"\n{title}: {result['status']} "
            f"({result['edge_count']} edges)"
        )
        print(result["message"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--serve",
        action="store_true",
        help="Start local coordinator for Unity and Role 1"
    )

    args = parser.parse_args()

    if args.serve:
        serve()
    else:
        run_demo()
