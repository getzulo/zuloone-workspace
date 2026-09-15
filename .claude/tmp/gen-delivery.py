# -*- coding: utf-8 -*-
import json
from pathlib import Path

ROOT = Path(r"D:\Sources\zuloone-workspace")
SALES = "47861dd2-1009-4926-9ea3-c506cae5118d"
QTY = "7c8bdcd2-b2d6-433e-8fb3-3d182a561200"
REF_OUTLET = "91560c1f-7be1-40df-a1e4-e65eaa794958"
REF_STORE = "0ef2de9a-0838-4472-9570-c709881b2be1"
REF_ROUTE = "97f7506f-aec0-46cd-acc3-37e26da6f6cb"
ROUTE = "9936cbb9-3fbb-415c-91bd-c7d8843b2f70"


def dump(rel, obj):
    path = ROOT / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(rel)


def seq(name, mid):
    return {
        "kind": "NumberSequence",
        "object": {
            "padLength": 0, "startValue": 1000, "increment": 1, "nextValue": 1000,
            "resetPolicy": "None",
            "metaId": mid, "name": name, "modelId": SALES,
        },
    }


def edt_enum(name, mid, enum_id, desc):
    return {
        "kind": "EDT",
        "object": {
            "edtType": "Enum",
            "description": desc,
            "hasTime": True, "hasTimezone": False, "dateOnly": False,
            "enumMetaId": enum_id, "isRequired": False,
            "metaId": mid, "name": name, "modelId": SALES,
        },
    }


def edt_ref(name, mid, dict_id):
    return {
        "kind": "EDT",
        "object": {
            "edtType": "Reference",
            "hasTime": True, "hasTimezone": False, "dateOnly": False,
            "referenceDictionaryMetaId": dict_id, "isRequired": False,
            "metaId": mid, "name": name, "modelId": SALES,
        },
    }


def enum_obj(name, mid, desc, values):
    return {
        "kind": "Enum",
        "object": {
            "description": desc, "isExtension": False,
            "metaId": mid, "name": name, "modelId": SALES,
        },
        "values": [
            {
                "enumMetaId": mid, "value": v, "displayOrder": v,
                "description": d, "metaId": vid, "name": n, "modelId": SALES,
            }
            for n, v, vid, d in values
        ],
    }


def field(dict_id, fname, mid, caption, caption_ru, caption_ar, order, **kw):
    row = {
        "dictionaryMetaId": dict_id,
        "fieldName": fname, "name": fname,
        "caption": caption, "caption_ru": caption_ru, "caption_ar": caption_ar,
        "isSystem": False, "isRequired": kw.get("req", False),
        "displayOrder": order, "isIndexed": kw.get("idx", False),
        "isUnique": kw.get("uniq", False),
        "isTranslatable": False, "isVisible": True, "isCalculated": False,
        "metaId": mid, "modelId": SALES,
    }
    if "edt" in kw:
        row["edtMetaId"] = kw["edt"]
    else:
        row["baseType"] = kw["bt"]
        if "len" in kw:
            row["length"] = kw["len"]
        if "prec" in kw:
            row["precision"] = kw["prec"]
            row["scale"] = kw["scale"]
    return row


def script(name, mid, object_type, object_name, object_meta):
    return {
        "kind": "Script",
        "object": {
            "scriptType": "EventHandler",
            "objectType": object_type,
            "objectName": object_name,
            "objectMetaId": object_meta,
            "executionOrder": 0,
            "metaId": mid, "name": name, "modelId": SALES,
        },
    }


def handler_cs(name, entity):
    return f"""#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class {name} : TypedDictionaryEventHandler<{entity}>
{{
    public override async Task<EventResult> OnBeforeSaveAsync({entity} record, bool isNew, EventContext context)
    {{
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;
        return EventResult.Ok();
    }}
}}
"""


# --- enums ---
dump("Sales/Enums/VehicleKind.json", enum_obj(
    "VehicleKind", "36669c7f-4499-484f-af75-ee9f309211e5",
    "Kind of delivery vehicle. Zero is Unspecified.",
    [
        ("Unspecified", 0, "9fb82350-2f10-4eda-89d8-c3e1301eef41", "Not set."),
        ("Van", 1, "1e182f32-0420-4750-85d4-87f18b45681d", "Van."),
        ("Truck", 2, "ad87fe25-160c-43e6-93fd-de2c903c9391", "Truck."),
        ("Bike", 3, "3f90127c-729b-4848-b0b8-20aedde79104", "Bike / scooter."),
        ("Car", 4, "4d17938c-7ef7-4408-b277-591064901af6", "Car."),
    ]))

dump("Sales/Enums/Weekday.json", enum_obj(
    "Weekday", "92085d19-3b9a-4ab9-bd00-bc6f998ffc03",
    "ISO weekday. Monday=1 … Sunday=7. Zero is Unspecified.",
    [
        ("Unspecified", 0, "449c3893-d218-447e-a36d-1083ba1a701f", "Not set."),
        ("Monday", 1, "90a78502-56f8-4c23-942c-e30509eaece0", "Monday."),
        ("Tuesday", 2, "09f43f82-0bb5-4420-8058-e8d3d3c6f3f7", "Tuesday."),
        ("Wednesday", 3, "0aa7d527-4332-4a80-ac48-5ca4422957ea", "Wednesday."),
        ("Thursday", 4, "d841b0be-0768-47ee-af6e-5613ebfbf07c", "Thursday."),
        ("Friday", 5, "636902d5-f3ad-4136-aa05-023105b6889a", "Friday."),
        ("Saturday", 6, "18edad5c-0f3a-455a-8e8a-7b8c2144267a", "Saturday."),
        ("Sunday", 7, "71ffac99-9af8-4ebe-b85f-af59f45e062c", "Sunday."),
    ]))

dump("Sales/Enums/StopOutcome.json", enum_obj(
    "StopOutcome", "99e79c96-042d-4156-9d48-0b6e454cc7dd",
    "What happened at a trip stop. Zero is Unspecified = delivered.",
    [
        ("Unspecified", 0, "89e3b5b4-0cd3-4540-8a11-8a0f13d47403", "Not set — treat as delivered."),
        ("Delivered", 1, "42bd584a-72d6-4d8d-94e7-a4178294c67f", "Delivered in full."),
        ("Partial", 2, "39e39a9b-9bc9-409c-a391-91aa76bdd0b8", "Partial delivery."),
        ("Refused", 3, "c1ae5ad6-616f-470a-b3b4-a703cdaffb07", "Customer refused — cancel the order."),
    ]))

dump("Sales/EDTs/VehicleKindEdt.json", edt_enum("VehicleKindEdt", "57f7df15-7aa0-4b7f-8cfd-3c2e837fb577", "36669c7f-4499-484f-af75-ee9f309211e5", "Kind of delivery vehicle"))
dump("Sales/EDTs/WeekdayEdt.json", edt_enum("WeekdayEdt", "cdd51c60-f7b0-4a3a-80c9-f501ea01e4eb", "92085d19-3b9a-4ab9-bd00-bc6f998ffc03", "ISO weekday"))
dump("Sales/EDTs/StopOutcomeEdt.json", edt_enum("StopOutcomeEdt", "449932de-5579-4181-99a9-abc747a3522b", "99e79c96-042d-4156-9d48-0b6e454cc7dd", "Trip stop outcome"))

dump("Sales/NumberSequences/VehicleSeq.json", seq("VehicleSeq", "7307595c-3aa6-4b8f-b22c-12effa7a8bcd"))
dump("Sales/NumberSequences/DriverSeq.json", seq("DriverSeq", "1edaffa1-044e-461e-a570-a4bcfbed46d4"))
dump("Sales/NumberSequences/DeliveryRouteStopSeq.json", seq("DeliveryRouteStopSeq", "096ae551-74bf-4037-962a-51060a06cfad"))
dump("Sales/NumberSequences/DeliveryScheduleSeq.json", seq("DeliveryScheduleSeq", "a0c35982-0a21-49bc-ada9-7f4cd031efa5"))

VEHICLE = "5956ee6f-d3bc-416d-b155-1315728f9884"
DRIVER = "2182e2bd-91d3-4a03-90cf-9ec47697d2b0"
STOP = "d6048607-6f9b-4758-9647-3cfbd5c7474c"
SCHED = "ac756f10-5c6d-4432-84dc-c361c082aef3"

dump("Sales/EDTs/RefVehicle.json", edt_ref("RefVehicle", "27eb594e-ddc9-4e2e-baf2-00f66cbad89c", VEHICLE))
dump("Sales/EDTs/RefDriver.json", edt_ref("RefDriver", "5714da5b-296e-4a05-ab8e-ad9bc683ee47", DRIVER))

dump("Sales/Dictionaries/Vehicle/Vehicle.object.json", {
    "kind": "Dictionary",
    "object": {
        "caption": "Vehicles", "caption_ru": "Транспорт", "caption_ar": "المركبات",
        "description": "Delivery vehicle: plate, kind, capacity.",
        "isLogged": True, "isHierarchical": False,
        "numberSequenceMetaId": "7307595c-3aa6-4b8f-b22c-12effa7a8bcd",
        "isVersioned": False, "isCached": False, "isSingleton": False,
        "notificationEnabled": False,
        "defaultSearchProperty": "PlateNumber",
        "displayFormat": "{PlateNumber} - {Name}",
        "isKernel": False,
        "iconName": "fluent-color:vehicle-truck-profile-48",
        "largeIconName": "fluent-color:vehicle-truck-profile-48",
        "isObsolete": False, "isExtension": False,
        "metaId": VEHICLE, "name": "Vehicle", "modelId": SALES,
    },
    "fields": [
        field(VEHICLE, "Name", "555f75da-72d7-416c-b42f-367380c5de73", "Name", "Наименование", "الاسم", 1, req=True, bt="String", len=160),
        field(VEHICLE, "PlateNumber", "68b131d6-794b-4b2b-80af-1edd33718436", "Plate", "Госномер", "اللوحة", 2, req=True, idx=True, uniq=True, bt="String", len=32),
        field(VEHICLE, "Kind", "815381c2-254c-432e-80b4-a3f494d26264", "Kind", "Тип", "النوع", 3, req=True, edt="57f7df15-7aa0-4b7f-8cfd-3c2e837fb577"),
        field(VEHICLE, "CapacityQty", "196aeceb-9d1a-4c20-aed5-c8a990792b39", "Capacity qty", "Вместимость, шт", "السعة", 4, edt=QTY),
        field(VEHICLE, "CapacityKg", "d9ae9c54-c991-43f5-8412-728b01e8d8f2", "Capacity kg", "Грузоподъёмность, кг", "الحمولة كغ", 5, bt="Decimal", prec=18, scale=3),
        field(VEHICLE, "IsDisabled", "7d9b227d-f855-4ebb-9fc0-db959fdd475c", "Disabled", "Отключён", "معطّل", 6, bt="Boolean"),
    ],
})

dump("Sales/Dictionaries/Driver/Driver.object.json", {
    "kind": "Dictionary",
    "object": {
        "caption": "Drivers", "caption_ru": "Водители", "caption_ar": "السائقون",
        "description": "Delivery driver. Not a sales agent and not an HR employee.",
        "isLogged": True, "isHierarchical": False,
        "numberSequenceMetaId": "1edaffa1-044e-461e-a570-a4bcfbed46d4",
        "isVersioned": False, "isCached": False, "isSingleton": False,
        "notificationEnabled": False,
        "defaultSearchProperty": "Name",
        "displayFormat": "{ID} - {Name}",
        "isKernel": False,
        "iconName": "fluent-color:person-available-24",
        "largeIconName": "fluent-color:person-available-24",
        "isObsolete": False, "isExtension": False,
        "metaId": DRIVER, "name": "Driver", "modelId": SALES,
    },
    "fields": [
        field(DRIVER, "Name", "0a755c98-1fd6-49b2-a85f-134531b77c50", "Name", "ФИО", "الاسم", 1, req=True, bt="String", len=160),
        field(DRIVER, "Phone", "ff0fa8f4-09d4-400b-be27-e13c39189664", "Phone", "Телефон", "الهاتف", 2, bt="String", len=32),
        field(DRIVER, "LicenseNumber", "3c679c48-d58f-4729-914c-9a6190a19230", "License", "Права", "الرخصة", 3, idx=True, bt="String", len=40),
        field(DRIVER, "IsDisabled", "22ae8cc0-4414-40f3-a774-4723f926d935", "Disabled", "Отключён", "معطّل", 4, bt="Boolean"),
    ],
})

dump("Sales/Dictionaries/DeliveryRouteStop/DeliveryRouteStop.object.json", {
    "kind": "Dictionary",
    "object": {
        "caption": "Route stops", "caption_ru": "Остановки маршрута", "caption_ar": "محطات المسار",
        "description": "Ordered outlet on a delivery route.",
        "isLogged": True, "isHierarchical": False,
        "numberSequenceMetaId": "096ae551-74bf-4037-962a-51060a06cfad",
        "isVersioned": False, "isCached": False, "isSingleton": False,
        "notificationEnabled": False,
        "defaultSearchProperty": "Sequence",
        "displayFormat": "{ID}",
        "isKernel": False,
        "iconName": "fluent-color:location-ripple-24",
        "largeIconName": "fluent-color:location-ripple-24",
        "isObsolete": False, "isExtension": False,
        "metaId": STOP, "name": "DeliveryRouteStop", "modelId": SALES,
    },
    "fields": [
        field(STOP, "Route", "afd2c99e-fb7c-4cc9-b105-ca4c9a97a805", "Route", "Маршрут", "المسار", 1, req=True, idx=True, edt=REF_ROUTE),
        field(STOP, "Sequence", "a6c44c95-2d36-4de3-b240-afe222f8125b", "Sequence", "Порядок", "الترتيب", 2, req=True, bt="Integer"),
        field(STOP, "Outlet", "c6dcd91f-8d26-4103-9817-f9060d4ae6b5", "Outlet", "Точка", "نقطة البيع", 3, req=True, idx=True, edt=REF_OUTLET),
        field(STOP, "DwellMinutes", "93e61ce7-aae6-467e-8e27-3adbe6703263", "Dwell, min", "Стоянка, мин", "الدقائق", 4, bt="Integer"),
        field(STOP, "WindowFromMinutes", "48174717-bd85-4e7c-918e-5c13e580c65f", "Window from, min", "Окно с, мин", "من دقيقة", 5, bt="Integer"),
        field(STOP, "WindowToMinutes", "3c5eab69-f835-4d58-82fa-0a8fa866443b", "Window to, min", "Окно по, мин", "إلى دقيقة", 6, bt="Integer"),
        field(STOP, "IsDisabled", "fa57aaaa-42d8-4cc6-b8f5-0ca20e3f06ba", "Disabled", "Отключена", "معطّل", 7, bt="Boolean"),
    ],
})

dump("Sales/Dictionaries/DeliverySchedule/DeliverySchedule.object.json", {
    "kind": "Dictionary",
    "object": {
        "caption": "Delivery schedules", "caption_ru": "Расписания доставки", "caption_ar": "جداول التوصيل",
        "description": "Recurring weekday wave: route, depart time, default crew.",
        "isLogged": True, "isHierarchical": False,
        "numberSequenceMetaId": "a0c35982-0a21-49bc-ada9-7f4cd031efa5",
        "isVersioned": False, "isCached": False, "isSingleton": False,
        "notificationEnabled": False,
        "defaultSearchProperty": "Name",
        "displayFormat": "{ID} - {Name}",
        "isKernel": False,
        "iconName": "fluent-color:calendar-clock-24",
        "largeIconName": "fluent-color:calendar-clock-24",
        "isObsolete": False, "isExtension": False,
        "metaId": SCHED, "name": "DeliverySchedule", "modelId": SALES,
    },
    "fields": [
        field(SCHED, "Name", "5fc7a54c-588e-433d-bd8e-648591212262", "Name", "Наименование", "الاسم", 1, req=True, bt="String", len=160),
        field(SCHED, "Route", "3568f519-f025-4697-b67c-e033de23c4bd", "Route", "Маршрут", "المسار", 2, req=True, idx=True, edt=REF_ROUTE),
        field(SCHED, "Weekday", "e8065ffd-92d9-4011-a5e2-e53232a19d11", "Weekday", "День недели", "اليوم", 3, req=True, edt="cdd51c60-f7b0-4a3a-80c9-f501ea01e4eb"),
        field(SCHED, "DepartHour", "79a75964-2057-4b0c-b6a8-10c6a21321d8", "Depart hour", "Час выезда", "ساعة الانطلاق", 4, bt="Integer"),
        field(SCHED, "DepartMinute", "138db67d-cb87-4293-8b59-fc46d2e32cc8", "Depart minute", "Минута выезда", "دقيقة الانطلاق", 5, bt="Integer"),
        field(SCHED, "Vehicle", "4db269f8-20c6-4cc7-9b32-dc51d94a4499", "Vehicle", "Машина", "المركبة", 6, edt="27eb594e-ddc9-4e2e-baf2-00f66cbad89c"),
        field(SCHED, "Driver", "43c46f4d-5fa8-4b29-b1aa-e717b305c45e", "Driver", "Водитель", "السائق", 7, edt="5714da5b-296e-4a05-ab8e-ad9bc683ee47"),
        field(SCHED, "IsDisabled", "07fefa24-847c-469a-8f8b-4c1f8dc2f567", "Disabled", "Отключено", "معطّل", 8, bt="Boolean"),
    ],
})

for name, entity, smid, oid in [
    ("VehicleEventHandler", "Vehicle", "fd1973be-fc90-414d-9fde-dea1c1267c8f", VEHICLE),
    ("DriverEventHandler", "Driver", "6aea935e-a700-4eb1-8ce5-4e526bc184d6", DRIVER),
    ("DeliveryRouteStopEventHandler", "DeliveryRouteStop", "926f1a82-b9b7-4830-a350-31cdd1517f2f", STOP),
    ("DeliveryScheduleEventHandler", "DeliverySchedule", "5ae00ec8-31e5-445f-8ab5-5e77beb333f2", SCHED),
]:
    folder = entity if entity in ("Vehicle", "Driver") else entity
    dump(f"Sales/Dictionaries/{folder}/Events/{name}.script.json", script(name, smid, "Dictionary", entity, oid))

dump("Sales/Services/DeliveryService/DeliveryService.json", {
    "kind": "Service",
    "object": {
        "description": "Рейс: штамп экипажа, набор точек с маршрута, волна по расписанию, проверки отправки.",
        "namespace": "ZuloOne.Services",
        "scriptMetaId": "95d94bed-0ab3-46fa-9915-857061b951b6",
        "metaId": "e649c2b9-066f-4d04-8b0f-eee4356956b1",
        "name": "DeliveryService",
        "modelId": SALES,
    },
})
dump("Sales/Services/DeliveryService/DeliveryService.script.json", {
    "kind": "Script",
    "object": {
        "scriptType": "Service", "objectType": "Service", "objectName": "DeliveryService",
        "executionOrder": 0,
        "metaId": "95d94bed-0ab3-46fa-9915-857061b951b6",
        "name": "DeliveryService",
        "modelId": SALES,
    },
})

dump("Sales/Commands/Document/FillTripFromRoute/FillTripFromRoute.json", {
    "kind": "DocumentCommand",
    "object": {
        "caption": "Fill from route", "caption_ru": "Набрать с маршрута", "caption_ar": "تعبئة من المسار",
        "scriptMetaId": "3e5b1711-e68f-4dfa-b553-ff189640ff7d",
        "parameterMode": "None", "displayOrder": 0, "beginGroup": False,
        "isEnabled": True, "requiresConfirmation": False, "reloadAfterExecution": True,
        "metaId": "8130d00b-830f-4e2d-b24c-c213c6e02d6b",
        "name": "FillTripFromRoute", "modelId": SALES,
    },
    "subtypeBindings": [
        {
            "metaId": "ee4e4141-8e2c-41d7-b19d-37384478af81",
            "documentCommandMetaId": "8130d00b-830f-4e2d-b24c-c213c6e02d6b",
            "documentSubtypeMetaId": "d9d69160-83c9-45de-a29e-af61a3828d16",
        }
    ],
})
dump("Sales/Commands/Document/FillTripFromRoute/FillTripFromRouteScript.script.json", {
    "kind": "Script",
    "object": {
        "scriptType": "DocumentCommand", "objectType": "Command",
        "objectName": "FillTripFromRoute",
        "objectMetaId": "8130d00b-830f-4e2d-b24c-c213c6e02d6b",
        "executionOrder": 0,
        "metaId": "3e5b1711-e68f-4dfa-b553-ff189640ff7d",
        "name": "FillTripFromRouteScript",
        "modelId": SALES,
    },
})

dump("Sales/Tests/DeliveryFleetTest.json", {
    "kind": "Test",
    "object": {
        "description": "Флот, остановки, расписание, набор рейса, занятость экипажа, отказ чужой точки.",
        "isAutoGenerated": False, "isActive": True,
        "groupName": "Бизнес-слой.Продажи и расчёты",
        "isExtension": False,
        "metaId": "09ae8326-c2df-4c2b-a56f-f21dfa45a8d8",
        "name": "DeliveryFleetTest",
        "modelId": SALES,
    },
})

print("done")
