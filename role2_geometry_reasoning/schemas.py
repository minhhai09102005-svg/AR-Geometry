"""Hợp đồng dữ liệu giữa Computer Vision, engine và ứng dụng AR """

import json
from dataclasses import asdict, dataclass, field
from types import SimpleNamespace
from typing import Any, Dict, List, Optional, Tuple


@dataclass
class VertexInput:
    """Một đỉnh CV nhận diện được, kèm vị trí trong hệ tọa độ 3D."""

    id: str
    x: float = 0.0
    y: float = 0.0
    z: float = 0.0
    confidence: float = 1.0


@dataclass
class CVDataInput:
    """Gói dữ liệu một frame mà Role 1 gửi sang bộ suy luận Role 2."""

    timestamp: float
    vertices: List[VertexInput]
    detected_edges: List[Tuple[str, str]]
    frame_id: int = 0
    target_shape: Optional[str] = None  # Hình học người dùng chọn trên UI Unity (nếu có)

    @classmethod
    def from_dict(cls, payload: Dict[str, Any]) -> "CVDataInput":
        """Đọc packet JSON hình học theo hợp đồng vertices + detected_edges."""
        if not isinstance(payload, dict):
            raise ValueError("Packet CV phải là một JSON object.")

        vertices_payload = payload.get("vertices")
        edges_payload = payload.get("detected_edges", payload.get("edges"))
        if not isinstance(vertices_payload, list) or not isinstance(edges_payload, list):
            raise ValueError(
                "Packet CV cần có danh sách 'vertices' và 'detected_edges' (hoặc 'edges'). "
                "Packet chỉ có hands/gesture chưa chứa cấu trúc hình học cho Role 2."
            )

        vertices: List[VertexInput] = []
        for index, item in enumerate(vertices_payload):
            if isinstance(item, VertexInput):
                vertices.append(item)
                continue
            if not isinstance(item, dict) or "id" not in item:
                raise ValueError(f"Đỉnh ở vị trí {index} phải là object có trường 'id'.")
            try:
                vertices.append(
                    VertexInput(
                        id=str(item["id"]),
                        x=float(item.get("x", 0.0)),
                        y=float(item.get("y", 0.0)),
                        z=float(item.get("z", 0.0)),
                        confidence=float(item.get("confidence", 1.0)),
                    )
                )
            except (TypeError, ValueError) as error:
                raise ValueError(f"Tọa độ đỉnh thứ {index} không hợp lệ.") from error

        detected_edges: List[Tuple[str, str]] = []
        for index, edge in enumerate(edges_payload):
            if not isinstance(edge, (list, tuple)) or len(edge) != 2:
                raise ValueError(f"Cạnh ở vị trí {index} phải gồm đúng hai ID đỉnh.")
            detected_edges.append((str(edge[0]), str(edge[1])))

        try:
            timestamp = float(payload.get("timestamp", 0.0))
            frame_id = int(payload.get("frame_id", payload.get("frameId", 0)))
        except (TypeError, ValueError) as error:
            raise ValueError("timestamp hoặc frame_id không hợp lệ.") from error

        target_shape = payload.get("target_shape", payload.get("shape"))
        return cls(
            timestamp=timestamp,
            frame_id=frame_id,
            vertices=vertices,
            detected_edges=detected_edges,
            target_shape=str(target_shape) if target_shape is not None else None,
        )


@dataclass
class ErrorDetail:
    """Danh sách lỗi và đỉnh cần đánh dấu để Unity hiển thị hướng dẫn sửa."""

    missing_vertices: List[str] = field(default_factory=list)
    extra_vertices: List[str] = field(default_factory=list)
    missing_edges: List[Tuple[str, str]] = field(default_factory=list)
    extra_edges: List[Tuple[str, str]] = field(default_factory=list)
    # Mỗi phần tử gồm (cạnh chuẩn bị thiếu nếu suy ra được, cạnh đang nối nhầm).
    wrong_connections: List[Tuple[Optional[Tuple[str, str]], Tuple[str, str]]] = field(default_factory=list)
    
    # [Tuần 6] Bổ sung chi tiết lỗi bậc đỉnh và danh sách đỉnh cần highlight đỏ trên AR Overlay
    degree_errors: Dict[str, int] = field(default_factory=dict)
    affected_vertices: List[str] = field(default_factory=list)


@dataclass
class ReasoningResult:
    shape: str
    status: str  # "valid", "invalid", "incomplete"
    is_correct: bool
    message: str
    details: ErrorDetail
    error_type: Optional[str] = None
    
    # [Tuần 5 & 7] Bổ sung độ tin cậy nhận diện và thông số hình học 3D (độ dài, diện tích, thể tích)
    confidence: float = 1.0
    metrics: Dict[str, Any] = field(default_factory=dict)
    frame_id: int = 0

    @property
    def correct(self) -> bool:
        """Tên ngắn gọn để các bên tích hợp có thể đọc trạng thái đúng/sai."""
        return self.is_correct

    @property
    def geometry(self) -> Any:
        """Cho phép truy cập số đo dạng thuộc tính mà vẫn giữ JSON metrics cũ."""
        return SimpleNamespace(**self.metrics)

    def to_json(self, indent: int = 2) -> str:
        """Xuất JSON có thêm alias ``correct`` và mã khung hình cho Unity."""
        payload = asdict(self)
        payload["correct"] = self.is_correct
        return json.dumps(payload, ensure_ascii=False, indent=indent)
