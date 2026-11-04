
"""Hợp đồng dữ liệu giữa Computer Vision, engine và ứng dụng AR."""

import json
from dataclasses import asdict, dataclass, field
from typing import Any, Dict, List, Optional, Tuple


@dataclass
class VertexInput:
    id: str
    x: float = 0.0
    y: float = 0.0
    z: float = 0.0
    confidence: float = 1.0


@dataclass
class CVDataInput:
    timestamp: float
    vertices: List[VertexInput]
    detected_edges: List[Tuple[str, str]]
    frame_id: int = 0
    target_shape: Optional[str] = None


@dataclass
class ErrorDetail:
    missing_vertices: List[str] = field(default_factory=list)
    extra_vertices: List[str] = field(default_factory=list)
    missing_edges: List[Tuple[str, str]] = field(default_factory=list)
    extra_edges: List[Tuple[str, str]] = field(default_factory=list)
    wrong_connections: List[Tuple[str, str]] = field(default_factory=list)

    degree_errors: Dict[str, int] = field(default_factory=dict)
    affected_vertices: List[str] = field(default_factory=list)


@dataclass
class ReasoningResult:
    shape: str
    status: str
    is_correct: bool
    message: str
    details: ErrorDetail
    error_type: Optional[str] = None

    confidence: float = 1.0
    metrics: Dict[str, Any] = field(default_factory=dict)

    # Đồng bộ metadata với Unity
    frame_id: int = 0
    vertex_count: int = 0
    edge_count: int = 0

    def to_json(self, indent: int = 2) -> str:
        """Xuất kết quả suy luận dưới dạng JSON."""
        return json.dumps(
            asdict(self),
            ensure_ascii=False,
            indent=indent
        )
