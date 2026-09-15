import json
import urllib.request

BASE = "http://localhost:5257"

BATCHES = [
    [
        "Purchasing/NumberSequences/PurchaseReturnSeq.json",
        "Purchasing/TableParts/PurchaseReturnLines.json",
        "Purchasing/TableParts/PurchaseReturnLines/Events/PurchaseReturnLinesEventHandler.script.json",
    ],
    [
        "Purchasing/Documents/PurchaseReturn/PurchaseReturn.object.json",
        "Purchasing/Commands/Document/PostPurchaseReturn/PostPurchaseReturn.json",
    ],
    [
        "Purchasing/Documents/PurchaseReturn/Events/PurchaseReturnEventHandler.script.json",
        "Purchasing/Documents/PurchaseReturn/Transactions/PurchaseReturnStockTx.script.json",
        "Purchasing/Documents/PurchaseReturn/Transactions/PurchaseReturnPayableTx.script.json",
        "Purchasing/Commands/Document/PostPurchaseReturn/PostPurchaseReturnScript.script.json",
    ],
    [
        "Purchasing/Documents/PurchaseReturn/PurchaseReturn.object.json",
        "Purchasing/Commands/Document/PostPurchaseReturn/PostPurchaseReturn.json",
        "Purchasing/Tests/PurchaseReturnTest.json",
        "Purchasing/Menu/menu.json",
        "Purchasing/model.json",
    ],
    [
        "GLIntegration/DocumentExtensions/PurchaseReturn.GLIntegration/PurchaseReturn.GLIntegration.extension.json",
        "GLIntegration/DocumentExtensions/PurchaseReturn.GLIntegration/PurchaseReturnGLEventHandler.script.json",
        "GLIntegration/model.json",
    ],
]


def post(path, payload, timeout=180):
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        BASE + path,
        data=data,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def get(path, timeout=30):
    with urllib.request.urlopen(BASE + path, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def main():
    status = get("/api/dev/workspace/sync-status")
    print("held=", status.get("held"), "reason=", (status.get("reason") or "")[:80])
    for i, files in enumerate(BATCHES, 1):
        result = post("/api/dev/workspace/apply-files", {"files": files, "force": True})
        out = {
            "created": result.get("created"),
            "updated": result.get("updated"),
            "unchanged": result.get("unchanged"),
            "refused": result.get("refused"),
            "errors": result.get("errors"),
        }
        print(f"batch {i}", json.dumps(out, ensure_ascii=True))
        if result.get("errors"):
            raise SystemExit(1)


if __name__ == "__main__":
    main()
