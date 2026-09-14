"""Hợp đồng dữ liệu giữa Computer Vision, engine và ứng dụng AR."""

import json
from dataclasses import asdict, dataclass, field
from typing import List, Optional, Tuple


@dataclass
class VertexInput:
    id: str
    x: float
    y: float
    z: float


@dataclass
class CVDataInput:
    timestamp: float
    vertices: List[VertexInput]
    detected_edges: List[Tuple[str, str]]


@dataclass
class ErrorDetail:
    missing_edges: List[Tuple[str, str]] = field(default_factory=list)
    extra_edges: List[Tuple[str, str]] = field(default_factory=list)
    wrong_connections: List[Tuple[str, str]] = field(default_factory=list)


@dataclass
class ReasoningResult:
    shape: str
    status: str
    is_correct: bool
    message: str
    details: ErrorDetail
    error_type: Optional[str] = None

    def to_json(self, indent: int = 2) -> str:
        """Xuất JSON tương thích với dữ liệu Unity."""
        return json.dumps(asdict(self), ensure_ascii=False, indent=indent)
