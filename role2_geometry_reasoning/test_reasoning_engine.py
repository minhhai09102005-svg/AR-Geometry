
"""Automated reasoning scenarios for Role 2."""

import json
from math import sqrt
import unittest

try:
    from .knowledge_base import KNOWLEDGE_BASE
    from .reasoning_engine import GeometryReasoningEngine
    from .schemas import CVDataInput, VertexInput
except ImportError:
    from knowledge_base import KNOWLEDGE_BASE
    from reasoning_engine import GeometryReasoningEngine
    from schemas import CVDataInput, VertexInput


def make_input(
    shape,
    edges=None,
    coordinates=None,
    marker_ids=False
):
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
        vertices[-1].id = "4"

    return CVDataInput(
        timestamp=123.5,
        frame_id=42,
        vertices=vertices,
        detected_edges=list(
            edges if edges is not None
            else knowledge.expected_edges
        )
    )


class GeometryReasoningTests(unittest.TestCase):

    def test_all_three_supported_shapes_validate(self):
        for shape in KNOWLEDGE_BASE:
            with self.subTest(shape=shape):
                result = GeometryReasoningEngine(
                    shape
                ).process(make_input(shape))

                self.assertEqual("valid", result.status)
                self.assertTrue(result.is_correct)

    def test_missing_edge_is_named(self):
        expected_edges = list(
            KNOWLEDGE_BASE["cube"].expected_edges
        )
        removed_edge = expected_edges[-1]
        edges = expected_edges[:-1]

        result = GeometryReasoningEngine(
            "cube"
        ).process(make_input("cube", edges))

        self.assertEqual(
            "missing_edge",
            result.error_type
        )
        self.assertEqual(
            [removed_edge],
            result.details.missing_edges
        )

    def test_replacement_edge_is_wrong_connection(self):
        edges = list(
            KNOWLEDGE_BASE["cube"].expected_edges
        )

        edges.remove(("G", "H"))
        edges.append(("A", "G"))

        result = GeometryReasoningEngine(
            "cube"
        ).process(make_input("cube", edges))

        self.assertEqual(
            "wrong_connection",
            result.error_type
        )
        self.assertEqual(
            [("A", "G")],
            result.details.wrong_connections
        )

    def test_marker_ids_and_reversed_edges(self):
        numeric_edges = [
            (
                str(
                    "4" if u == "S"
                    else ord(u) - ord("A")
                ),
                str(
                    "4" if v == "S"
                    else ord(v) - ord("A")
                )
            )
            for u, v in KNOWLEDGE_BASE[
                "square_pyramid"
            ].expected_edges
        ]

        result = GeometryReasoningEngine(
            "square_pyramid"
        ).process(
            make_input(
                "square_pyramid",
                marker_ids=True,
                edges=[
                    (v, u)
                    for u, v in numeric_edges
                ]
            )
        )

        self.assertEqual("valid", result.status)

    def test_geometrically_skewed_cube(self):
        points = {
            "A": (0, 0, 0),
            "B": (1, 0, 0),
            "C": (1, 1, 0),
            "D": (0, 1, 0),
            "E": (0, 0, 1),
            "F": (1, 0, 1),
            "G": (1, 1.5, 1),
            "H": (0, 1, 1),
        }

        result = GeometryReasoningEngine(
            "cube"
        ).process(
            make_input(
                "cube",
                coordinates=points
            )
        )

        self.assertEqual(
            "geometry_distortion",
            result.error_type
        )
        self.assertFalse(result.is_correct)

    def test_auto_identifies_cube_and_prism(self):
        cube_points = {
            "A": (0, 0, 0),
            "B": (1, 0, 0),
            "C": (1, 1, 0),
            "D": (0, 1, 0),
            "E": (0, 0, 1),
            "F": (1, 0, 1),
            "G": (1, 1, 1),
            "H": (0, 1, 1),
        }

        prism_points = {
            key: (x * 2, y, z * 3)
            for key, (x, y, z)
            in cube_points.items()
        }

        cube = GeometryReasoningEngine(
            target_shape="auto"
        ).process(
            make_input(
                "cube",
                coordinates=cube_points
            )
        )

        prism = GeometryReasoningEngine(
            target_shape="auto"
        ).process(
            make_input(
                "rectangular_prism",
                coordinates=prism_points
            )
        )

        self.assertEqual("cube", cube.shape)
        self.assertEqual(
            "rectangular_prism",
            prism.shape
        )

    def test_metrics_and_json_fields(self):
        points = {
            "A": (0, 0, 0),
            "B": (1, 0, 0),
            "C": (1, 1, 0),
            "D": (0, 1, 0),
            "E": (0, 0, 1),
            "F": (1, 0, 1),
            "G": (1, 1, 1),
            "H": (0, 1, 1),
        }

        result = GeometryReasoningEngine(
            "cube"
        ).process(
            make_input(
                "cube",
                coordinates=points
            )
        )

        self.assertEqual(
            6.0,
            result.metrics["surface_area"]
        )
        self.assertEqual(
            1.0,
            result.metrics["volume"]
        )
        self.assertIn(
            "surface_area",
            result.metrics
        )

        payload = json.loads(
            result.to_json()
        )

        self.assertTrue(payload["is_correct"])
        self.assertEqual(42, payload["frame_id"])
        self.assertEqual(8, payload["vertex_count"])
        self.assertEqual(12, payload["edge_count"])

        self.assertIn("details", payload)
        self.assertIn("metrics", payload)

    def test_prism_and_pyramid_area_and_volume(self):
        cube_points = {
            "A": (0, 0, 0),
            "B": (1, 0, 0),
            "C": (1, 1, 0),
            "D": (0, 1, 0),
            "E": (0, 0, 1),
            "F": (1, 0, 1),
            "G": (1, 1, 1),
            "H": (0, 1, 1),
        }

        prism_points = {
            key: (x * 2, y, z * 3)
            for key, (x, y, z)
            in cube_points.items()
        }

        prism = GeometryReasoningEngine(
            "rectangular_prism"
        ).process(
            make_input(
                "rectangular_prism",
                coordinates=prism_points
            )
        )

        self.assertAlmostEqual(
            22,
            prism.metrics["surface_area"]
        )
        self.assertAlmostEqual(
            6,
            prism.metrics["volume"]
        )

        pyramid_points = {
            "A": (-1, -1, 0),
            "B": (1, -1, 0),
            "C": (1, 1, 0),
            "D": (-1, 1, 0),
            "S": (0, 0, sqrt(2)),
        }

        pyramid = GeometryReasoningEngine(
            "square_pyramid"
        ).process(
            make_input(
                "square_pyramid",
                coordinates=pyramid_points
            )
        )


        self.assertAlmostEqual(
            4 + 4 * sqrt(3),
            pyramid.metrics["surface_area"],
            places=4
        )

        self.assertAlmostEqual(
            4 * sqrt(2) / 3,
            pyramid.metrics["volume"],
            places=4
        )



if __name__ == "__main__":
    unittest.main()
