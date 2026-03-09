from __future__ import annotations

import json
from fastapi import FastAPI, Request

app = FastAPI(title="Agent Event Test Service")


@app.get("/health")
async def health() -> dict:
    return {"status": "ok"}


@app.post("/agent/event")
async def agent_event(request: Request) -> dict:
    payload = await request.json()
    payload_text = json.dumps(payload, ensure_ascii=False)
    print(f"[POST /agent/event] recv: {payload_text[:500]}")

    # 为了验证 Unity -> Python -> Unity 链路，固定回一条可被 ApplyAgentEvent 处理的事件
    return {
        "events": [
            {
                "type": "map_update_proposal",
                "nodeId": "n1",
                "proposedStatus": "completed",
                "confidence": 0.9,
                "evidence": {
                    "sourceRoleId": "python-agent",
                    "quote": "done",
                    "rule": "python_echo_ok",
                    "confidence": 0.9,
                },
            }
        ]
    }
