import json
import urllib.request

BASE = "http://localhost:5257"

BATCHES = [
    [
        "Sales/Enums/VehicleKind.json",
        "Sales/Enums/Weekday.json",
        "Sales/Enums/StopOutcome.json",
        "Sales/EDTs/VehicleKindEdt.json",
        "Sales/EDTs/WeekdayEdt.json",
        "Sales/EDTs/StopOutcomeEdt.json",
        "Sales/NumberSequences/VehicleSeq.json",
        "Sales/NumberSequences/DriverSeq.json",
        "Sales/NumberSequences/DeliveryRouteStopSeq.json",
        "Sales/NumberSequences/DeliveryScheduleSeq.json",
        "Sales/Dictionaries/Vehicle/Vehicle.object.json",
        "Sales/Dictionaries/Driver/Driver.object.json",
        "Sales/EDTs/RefVehicle.json",
        "Sales/EDTs/RefDriver.json",
        "Sales/Dictionaries/DeliveryRouteStop/DeliveryRouteStop.object.json",
        "Sales/Dictionaries/DeliverySchedule/DeliverySchedule.object.json",
    ],
    [
        "Sales/Dictionaries/Vehicle/Events/VehicleEventHandler.script.json",
        "Sales/Dictionaries/Driver/Events/DriverEventHandler.script.json",
        "Sales/Dictionaries/DeliveryRouteStop/Events/DeliveryRouteStopEventHandler.script.json",
        "Sales/Dictionaries/DeliverySchedule/Events/DeliveryScheduleEventHandler.script.json",
        "Sales/Dictionaries/DeliveryRoute/Events/DeliveryRouteEventHandler.script.json",
        "Sales/Dictionaries/DeliveryRoute/DeliveryRoute.object.json",
        "Sales/Documents/DeliveryTrip/DeliveryTrip.object.json",
        "Sales/TableParts/DeliveryTripLines.json",
        "Sales/Documents/DeliveryTrip/Events/DeliveryTripEventHandler.script.json",
        "Sales/TableParts/DeliveryTripLines/Events/DeliveryTripLinesEventHandler.script.json",
        "Sales/Services/DeliveryService/DeliveryService.script.json",
        "Sales/Services/DeliveryService/DeliveryService.json",
        "Sales/Services/SalesFulfillmentService/SalesFulfillmentService.script.json",
        "Sales/Commands/Document/FillTripFromRoute/FillTripFromRouteScript.script.json",
        "Sales/Commands/Document/FillTripFromRoute/FillTripFromRoute.json",
        "Sales/Commands/Document/DispatchTrip/DispatchTripScript.script.json",
        "Sales/Commands/Document/CompleteTrip/CompleteTripScript.script.json",
        "Sales/Commands/User/PlanDeliveryWave/PlanDeliveryWaveScript.script.json",
        "Sales/Commands/User/PlanDeliveryWave/PlanDeliveryWave.json",
        "Sales/Tests/DeliveryFleetTest.json",
        "Sales/Menu/menu.json",
        "Sales/model.json",
    ],
]


def post(path, payload):
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        BASE + path,
        data=data,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=120) as resp:
        return json.loads(resp.read().decode("utf-8"))


def main():
    raw = urllib.request.urlopen(BASE + "/api/dev/workspace/sync-status", timeout=15).read().decode("utf-8")
    status = json.loads(raw)
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
