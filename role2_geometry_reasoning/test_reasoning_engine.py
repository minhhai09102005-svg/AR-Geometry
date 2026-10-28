"""Automated reasoning scenarios required for the Role 2 handoff."""

import json
from math import sqrt
import unittest

try:
    from .knowledge_base import KNOWLEDGE_BASE
    from .reasoning_engine import GeometryReasoningEngine
    from .schemas import CVDataInput, VertexInput
except ImportError:  # pragma: no cover - allow ``python -m unittest`` in this directory
    from knowledge_base import KNOWLEDGE_BASE
    from reasoning_engine import GeometryReasoningEngine
    from schemas import CVDataInput, VertexInput


def make_input(shape, edges=None, coordinates=None, marker_ids=False):
    knowledge = KNOWLEDGE_BASE[shape]
    names = knowledge.expected_vertices
    coordinates = coordinates or {}
    vertices = [
        VertexInput(
            id=str(names.index(name)) if marker_ids else name,
            x=coordinates.get(name, (0.0, 0.0, 0.0))[0],
            y=coordinates.get(name, (0.0, 0.0, 0.0))[1],
            z=coordinates.get(name, (0.0, 0.0, 0.0))[2],
        )
        for name in names
    ]
    if marker_ids and shape == "square_pyramid":
        vertices[-1].id = "4"  # Role 1 may reuse marker ID 4 for apex S.
    return CVDataInput(timestamp=123.5, frame_id=42, vertices=vertices,
                       detected_edges=list(edges if edges is not None else knowledge.expected_edges))


class GeometryReasoningTests(unittest.TestCase):
    def test_all_three_supported_shapes_validate(self):
        for shape in KNOWLEDGE_BASE:
            with self.subTest(shape=shape):
                result = GeometryReasoningEngine(shape).process(make_input(shape))
                self.assertEqual("valid", result.status)
                self.assertTrue(result.correct)

    def test_missing_edge_is_named(self):
        # Chọn cạnh cụ thể thay vì phụ thuộc thứ tự các cạnh trong knowledge base.
        edges = list(KNOWLEDGE_BASE["cube"].expected_edges)
        edges.remove(("G", "H"))
        result = GeometryReasoningEngine("cube").process(make_input("cube", edges))
        self.assertEqual("missing_edge", result.error_type)
        self.assertEqual([("G", "H")], result.details.missing_edges)

    def test_replacement_edge_is_reported_as_wrong_connection(self):
        edges = list(KNOWLEDGE_BASE["cube"].expected_edges)
        edges.remove(("G", "H"))
        edges.append(("A", "G"))
        result = GeometryReasoningEngine("cube").process(make_input("cube", edges))
        self.assertEqual("wrong_connection", result.error_type)
        self.assertEqual([(("G", "H"), ("A", "G"))], result.details.wrong_connections)

    def test_marker_ids_and_reversed_edges_are_normalized(self):
        numeric_edges = [(str("4" if u == "S" else ord(u) - ord("A")),
                          str("4" if v == "S" else ord(v) - ord("A")))
                         for u, v in KNOWLEDGE_BASE["square_pyramid"].expected_edges]
        result = GeometryReasoningEngine("square_pyramid").process(
            make_input("square_pyramid", marker_ids=True,
                       edges=[(v, u) for u, v in numeric_edges])
        )
        self.assertEqual("valid", result.status)

    def test_geometrically_skewed_cube_is_flagged(self):
        points = {
            "A": (0, 0, 0), "B": (1, 0, 0), "C": (1, 1, 0), "D": (0, 1, 0),
            "E": (0, 0, 1), "F": (1, 0, 1), "G": (1, 1.5, 1), "H": (0, 1, 1),
        }
        result = GeometryReasoningEngine("cube").process(make_input("cube", coordinates=points))
        self.assertEqual("geometry_mismatch", result.error_type)
        self.assertFalse(result.is_correct)

    def test_graph_identification_distinguishes_cube_and_prism_with_coordinates(self):
        cube_points = {
            "A": (0, 0, 0), "B": (1, 0, 0), "C": (1, 1, 0), "D": (0, 1, 0),
            "E": (0, 0, 1), "F": (1, 0, 1), "G": (1, 1, 1), "H": (0, 1, 1),
        }
        prism_points = {key: (x * 2, y, z * 3) for key, (x, y, z) in cube_points.items()}
        cube = GeometryReasoningEngine().process(make_input("cube", coordinates=cube_points))
        prism = GeometryReasoningEngine().process(make_input("rectangular_prism", coordinates=prism_points))
        self.assertEqual("cube", cube.shape)
        self.assertEqual("rectangular_prism", prism.shape)

    def test_geometry_metrics_and_formula_are_included(self):
        points = {
            "A": (0, 0, 0), "B": (1, 0, 0), "C": (1, 1, 0), "D": (0, 1, 0),
            "E": (0, 0, 1), "F": (1, 0, 1), "G": (1, 1, 1), "H": (0, 1, 1),
        }
        result = GeometryReasoningEngine("cube").process(make_input("cube", coordinates=points))
        self.assertEqual(6.0, result.geometry.surface_area)
        self.assertEqual(1.0, result.geometry.volume)
        self.assertIn("surface_area", result.geometry.formulas)
        payload = json.loads(result.to_json())
        self.assertTrue(payload["correct"])
        self.assertEqual(42, payload["frame_id"])

    def test_json_contract_accepts_geometry_packet_and_rejects_hand_packet(self):
        geometry_packet = {
            "timestamp": 123.5,
            "frameId": 42,
            "target_shape": "cube",
            "vertices": [{"id": name} for name in "ABCDEFGH"],
            "detected_edges": list(KNOWLEDGE_BASE["cube"].expected_edges),
        }
        response = json.loads(GeometryReasoningEngine().process_json(json.dumps(geometry_packet)))
        self.assertTrue(response["correct"])
        self.assertEqual(42, response["frame_id"])

        # Role 1 hand tracking packet chỉ có gesture, chưa có graph hình học.
        with self.assertRaisesRegex(ValueError, "chưa chứa cấu trúc hình học"):
            GeometryReasoningEngine().process_json(json.dumps({"hands": [], "frame_id": 42}))

    def test_prism_and_pyramid_area_and_volume(self):
        cube_points = {
            "A": (0, 0, 0), "B": (1, 0, 0), "C": (1, 1, 0), "D": (0, 1, 0),
            "E": (0, 0, 1), "F": (1, 0, 1), "G": (1, 1, 1), "H": (0, 1, 1),
        }
        prism_points = {key: (x * 2, y, z * 3) for key, (x, y, z) in cube_points.items()}
        prism = GeometryReasoningEngine("rectangular_prism").process(make_input("rectangular_prism", coordinates=prism_points))
        self.assertAlmostEqual(22, prism.geometry.surface_area)
        self.assertAlmostEqual(6, prism.geometry.volume)

        pyramid_points = {
            "A": (-1, -1, 0), "B": (1, -1, 0), "C": (1, 1, 0), "D": (-1, 1, 0),
            "S": (0, 0, sqrt(2)),
        }
        pyramid = GeometryReasoningEngine("square_pyramid").process(make_input("square_pyramid", coordinates=pyramid_points))
        self.assertAlmostEqual(4 + 4 * sqrt(3), pyramid.geometry.surface_area)
        self.assertAlmostEqual(4 * sqrt(2) / 3, pyramid.geometry.volume)


if __name__ == "__main__":
    unittest.main()
