# One-shot generator for the Learning model metadata. Not part of the product.
import json, uuid
from pathlib import Path

root = Path(r"d:\Sources\zuloone-workspace\Learning")
MODEL = "c4a8e2f1-7b36-4d19-8e50-6a1f9c3d72b8"

ids = {}

def gid(key):
    if key not in ids:
        ids[key] = str(uuid.uuid4())
    return ids[key]

def cap(en, ru):
    return {"en": en, "ru": ru}

def dump(rel, obj):
    path = root / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

def dict_obj(name, en, ru, description, fields):
    mid = gid("dict:" + name)
    dump(f"Dictionaries/{name}/{name}.object.json", {
        "kind": "Dictionary",
        "object": {
            "caption": cap(en, ru),
            "description": description,
            "isHierarchical": False,
            "defaultSearchProperty": "Name",
            "displayFormat": "{Name}",
            "isLogged": False, "isVersioned": False, "isCached": False,
            "metaId": mid, "name": name, "modelId": MODEL,
        },
        "fields": fields(mid),
    })
    return mid

def field(owner, name, en, ru, order, **extra):
    body = {
        "dictionaryMetaId": owner,
        "fieldName": name, "name": name,
        "caption": cap(en, ru),
        "isRequired": extra.pop("isRequired", False),
        "displayOrder": order, "isVisible": True,
        "isIndexed": extra.pop("isIndexed", False),
        "isUnique": extra.pop("isUnique", False),
        "metaId": gid(f"field:{owner}:{name}"),
        "modelId": MODEL,
    }
    body.update(extra)
    return body

def ref_edt(dict_name):
    dump(f"EDTs/Ref{dict_name}.json", {
        "kind": "EDT",
        "object": {
            "edtType": "Reference",
            "referenceDictionaryMetaId": gid("dict:" + dict_name),
            "metaId": gid("edt:Ref" + dict_name),
            "name": "Ref" + dict_name,
            "modelId": MODEL,
        },
    })

def enum_edt(enum_name):
    dump(f"EDTs/{enum_name}Edt.json", {
        "kind": "EDT",
        "object": {
            "edtType": "Enum",
            "enumMetaId": gid("enum:" + enum_name),
            "metaId": gid("edt:" + enum_name),
            "name": enum_name + "Edt",
            "modelId": MODEL,
        },
    })

# --- model ---
dump("model.json", {
    "kind": "Model",
    "object": {
        "modelType": "Custom",
        "description": "Каталог обучения: версии уроков и экзаменов, история прохождения, попытки и сертификаты.",
        "modelVersion": "1.0.0",
        "publisher": "ZuloOne",
        "isSystem": False, "isSealed": False, "isEnabled": True,
        "metaId": MODEL, "name": "Learning",
        "modelId": "00000000-0000-0000-0000-000000000000",
        "layerId": 2,
    },
    "dependencies": [],
})

# --- enums ---
def write_enum(name, description, values):
    eid = gid("enum:" + name)
    dump(f"Enums/{name}.json", {
        "kind": "Enum",
        "object": {
            "description": description,
            "isExtension": False,
            "metaId": eid, "name": name, "modelId": MODEL,
        },
        "values": [
            {
                "enumMetaId": eid, "value": i, "displayOrder": i,
                "description": text, "metaId": gid(f"enumval:{name}:{code}"),
                "name": code, "modelId": MODEL,
            }
            for i, (code, text) in enumerate(values)
        ],
    })

write_enum("UnitKind", "Page or end-of-module check. Zero is Unspecified so an empty field is not a lesson.",
           [("Unspecified", "Not set"), ("Lesson", "Lesson page"), ("Check", "Knowledge check")])
write_enum("LearningEventKind", "What the learner did on a page version. Zero is Unspecified.",
           [("Unspecified", "Not set"), ("Opened", "Opened the page"), ("CheckPassed", "Passed the check"), ("CheckFailed", "Missed the check")])
write_enum("EnrollmentSource", "Who put the learner on the path. Zero is Unspecified.",
           [("Unspecified", "Not set"), ("Self", "Enrolled themselves"), ("Assigned", "Assigned by an owner")])

for n in ("UnitKind", "LearningEventKind", "EnrollmentSource"):
    enum_edt(n)

dump("EDTs/LearningDateTime.json", {
    "kind": "EDT",
    "object": {
        "edtType": "DateTime", "metaId": gid("edt:LearningDateTime"),
        "name": "LearningDateTime", "modelId": MODEL,
    },
})
dump("EDTs/LearningFlag.json", {
    "kind": "EDT",
    "object": {
        "edtType": "Boolean", "metaId": gid("edt:LearningFlag"),
        "name": "LearningFlag", "modelId": MODEL,
    },
})

# dictionary ids are allocated by dict_obj; EDTs that point at them must be written AFTER dict_obj
# so call dict_obj first, then ref_edt.

def str_field(owner, name, en, ru, order, length=256, required=False, unique=False):
    return field(owner, name, en, ru, order, baseType="String", length=length,
                 isRequired=required, isIndexed=unique or required, isUnique=unique)

def int_field(owner, name, en, ru, order):
    return field(owner, name, en, ru, order, baseType="Integer")

def date_field(owner, name, en, ru, order):
    return field(owner, name, en, ru, order, baseType="DateTime")

def text_field(owner, name, en, ru, order, large=False):
    return field(owner, name, en, ru, order, baseType="LargeText" if large else "Text", length=4000)

def ref_field(owner, name, en, ru, order, target, required=False):
    return field(owner, name, en, ru, order, edtMetaId=gid("edt:Ref" + target), isRequired=required, isIndexed=True)

def enum_field(owner, name, en, ru, order, enum_name):
    return field(owner, name, en, ru, order, edtMetaId=gid("edt:" + enum_name))

dicts = [
    ("Organization", "Organization", "Организация", "Стенд, к которому привязан учащийся.",
     lambda o: [str_field(o, "Name", "Name", "Наименование", 1, required=True),
                str_field(o, "StandSlug", "Stand", "Стенд", 2, length=64, unique=True)]),
    ("Learner", "Learner", "Учащийся", "Человек каталога. Ключ — почта.",
     lambda o: [str_field(o, "Email", "Email", "Почта", 1, required=True, unique=True),
                str_field(o, "Name", "Name", "Имя", 2)]),
    ("Track", "Learning path", "Путь", "Карточка навыка. Текст лежит в версии.",
     lambda o: [str_field(o, "StableId", "Stable id", "Стабильный код", 1, required=True, unique=True),
                str_field(o, "Name", "Name", "Наименование", 2),
                ref_field(o, "PublishedRevision", "Published revision", "Опубликованная версия", 3, "TrackRevision")]),
    ("TrackRevision", "Path revision", "Версия пути", "Неизменяемая редакция пути.",
     lambda o: [ref_field(o, "Track", "Path", "Путь", 1, "Track", required=True),
                int_field(o, "Number", "Number", "Номер", 2),
                str_field(o, "Name", "Name", "Наименование", 3),
                date_field(o, "PublishedAt", "Published", "Опубликована", 4)]),
    ("Module", "Module", "Модуль", "Карточка модуля. Текст лежит в версии.",
     lambda o: [str_field(o, "StableId", "Stable id", "Стабильный код", 1, required=True, unique=True),
                str_field(o, "Name", "Name", "Наименование", 2),
                ref_field(o, "PublishedRevision", "Published revision", "Опубликованная версия", 3, "ModuleRevision")]),
    ("ModuleRevision", "Module revision", "Версия модуля", "Неизменяемая редакция модуля.",
     lambda o: [ref_field(o, "Module", "Module", "Модуль", 1, "Module", required=True),
                int_field(o, "Number", "Number", "Номер", 2),
                str_field(o, "Name", "Name", "Наименование", 3),
                int_field(o, "Minutes", "Minutes", "Минуты", 4),
                str_field(o, "ArticlePath", "Article", "Статья", 5, length=512),
                str_field(o, "ArticleRevision", "Article revision", "Редакция статьи", 6, length=64),
                date_field(o, "PublishedAt", "Published", "Опубликована", 7)]),
    ("Unit", "Page", "Страница", "Карточка страницы урока.",
     lambda o: [str_field(o, "StableId", "Stable id", "Стабильный код", 1, required=True, unique=True),
                str_field(o, "Name", "Name", "Наименование", 2),
                ref_field(o, "PublishedRevision", "Published revision", "Опубликованная версия", 3, "UnitRevision")]),
    ("UnitRevision", "Page revision", "Версия страницы", "Неизменяемый текст страницы.",
     lambda o: [ref_field(o, "Unit", "Page", "Страница", 1, "Unit", required=True),
                ref_field(o, "ModuleRevision", "Module revision", "Версия модуля", 2, "ModuleRevision"),
                int_field(o, "Number", "Number", "Номер", 3),
                str_field(o, "Name", "Name", "Наименование", 4),
                text_field(o, "Body", "Body", "Текст", 5, large=True),
                enum_field(o, "Kind", "Kind", "Вид", 6, "UnitKind"),
                int_field(o, "SortOrder", "Order", "Порядок", 7),
                date_field(o, "PublishedAt", "Published", "Опубликована", 8)]),
    ("Exam", "Exam", "Экзамен", "Карточка экзамена. Вопросы лежат в версии.",
     lambda o: [str_field(o, "Name", "Name", "Наименование", 1, required=True),
                ref_field(o, "Track", "Path", "Путь", 2, "Track"),
                ref_field(o, "PublishedRevision", "Published revision", "Опубликованная версия", 3, "ExamRevision"),
                ref_field(o, "DraftRevision", "Draft revision", "Черновик", 4, "ExamRevision")]),
    ("ExamRevision", "Exam revision", "Версия экзамена", "Неизменяемый лист экзамена.",
     lambda o: [ref_field(o, "Exam", "Exam", "Экзамен", 1, "Exam", required=True),
                int_field(o, "Number", "Number", "Номер", 2),
                int_field(o, "PassPercent", "Pass percent", "Порог, %", 3),
                int_field(o, "ValidDays", "Valid days", "Срок, дни", 4),
                date_field(o, "PublishedAt", "Published", "Опубликована", 5)]),
    ("CheckPrompt", "Check question", "Вопрос проверки", "Вопрос версии страницы-проверки.",
     lambda o: [ref_field(o, "UnitRevision", "Page revision", "Версия страницы", 1, "UnitRevision", required=True),
                int_field(o, "SortOrder", "Order", "Порядок", 2),
                str_field(o, "Name", "Name", "Наименование", 3),
                text_field(o, "Text", "Text", "Текст", 4, large=True)]),
    ("CheckOption", "Check option", "Вариант проверки", "Вариант ответа. Верный помечен.",
     lambda o: [ref_field(o, "Prompt", "Question", "Вопрос", 1, "CheckPrompt", required=True),
                int_field(o, "SortOrder", "Order", "Порядок", 2),
                str_field(o, "Name", "Name", "Наименование", 3),
                text_field(o, "Text", "Text", "Текст", 4),
                field(o, "IsCorrect", "Correct", "Верный", 5, baseType="Boolean")]),
    ("ExamQuestion", "Exam question", "Вопрос экзамена", "Вопрос одной версии экзамена.",
     lambda o: [ref_field(o, "ExamRevision", "Exam revision", "Версия экзамена", 1, "ExamRevision", required=True),
                int_field(o, "SortOrder", "Order", "Порядок", 2),
                str_field(o, "Name", "Name", "Наименование", 3),
                text_field(o, "Text", "Text", "Текст", 4, large=True)]),
    ("ExamOption", "Exam option", "Вариант экзамена", "Вариант ответа версии экзамена.",
     lambda o: [ref_field(o, "Question", "Question", "Вопрос", 1, "ExamQuestion", required=True),
                int_field(o, "SortOrder", "Order", "Порядок", 2),
                str_field(o, "Name", "Name", "Наименование", 3),
                text_field(o, "Text", "Text", "Текст", 4),
                field(o, "IsCorrect", "Correct", "Верный", 5, baseType="Boolean")]),
]

for spec in dicts:
    dict_obj(*spec)

for name, *_ in dicts:
    ref_edt(name)

# --- links ---
def link(name, en, ru, props):
    mid = gid("link:" + name)
    dump(f"LinkTables/{name}.json", {
        "kind": "LinkTable",
        "object": {
            "caption": cap(en, ru), "tableName": "LT_" + name,
            "isKernel": False, "isLogged": False,
            "metaId": mid, "name": name, "modelId": MODEL,
        },
        "properties": props(mid),
    })

def lprop(owner, name, en, ru, order, **extra):
    body = {
        "linkTableMetaId": owner, "fieldName": name, "name": name,
        "caption": cap(en, ru),
        "isRequired": extra.pop("isRequired", False),
        "isPrimaryKey": False, "isLogged": False,
        "isIndexed": extra.get("isReference", False),
        "isReference": extra.get("isReference", False),
        "displayOrder": order,
        "metaId": gid(f"lprop:{owner}:{name}"), "modelId": MODEL,
    }
    body.update(extra)
    return body

def lref(owner, name, en, ru, order, target):
    return lprop(owner, name, en, ru, order, isRequired=True, isReference=True,
                 referenceKind="Dictionary", referenceMetaId=gid("dict:" + target))

link("Membership", "Membership", "Участие",
     lambda o: [lref(o, "Learner", "Learner", "Учащийся", 1, "Learner"),
                lref(o, "Organization", "Organization", "Организация", 2, "Organization")])
link("TrackRevisionModule", "Path modules", "Модули пути",
     lambda o: [lref(o, "TrackRevision", "Path revision", "Версия пути", 1, "TrackRevision"),
                lref(o, "ModuleRevision", "Module revision", "Версия модуля", 2, "ModuleRevision"),
                lprop(o, "SortOrder", "Order", "Порядок", 3, baseType="Integer")])

# --- registers ---
def register(name, en, ru, description, dims, resources):
    mid = gid("reg:" + name)
    dump(f"Registers/{name}/{name}.object.json", {
        "kind": "Register",
        "object": {
            "caption": cap(en, ru), "description": description,
            "registerEngineType": "Information",
            "isDoubleEntry": False, "allowNegativeBalance": True, "useBalanceTable": False,
            "metaId": mid, "name": name, "modelId": MODEL,
        },
        "dimensions": [reg_field(mid, *d, operational=True) for d in dims],
        "resources": [reg_field(mid, *r, operational=False) for r in resources],
    })

def reg_field(owner, name, en, ru, order, edt, operational):
    return {
        "registerMetaId": owner, "fieldName": name, "name": name,
        "caption": cap(en, ru), "edtMetaId": edt,
        "isOperational": operational, "displayOrder": order,
        "metaId": gid(f"regf:{owner}:{name}"), "modelId": MODEL,
    }

register("LearningEvent", "Learning history", "История обучения",
         "Каждое открытие и каждая проверка — новый период. Срез последний — где остановились.",
         [("Learner", "Learner", "Учащийся", 1, gid("edt:RefLearner")),
          ("UnitRevision", "Page revision", "Версия страницы", 2, gid("edt:RefUnitRevision"))],
         [("Kind", "Kind", "Событие", 1, gid("edt:LearningEventKind"))])
register("ModuleAward", "Module badge", "Значок модуля",
         "Значок за конкретную версию модуля. Новая версия его не закрывает.",
         [("Learner", "Learner", "Учащийся", 1, gid("edt:RefLearner")),
          ("ModuleRevision", "Module revision", "Версия модуля", 2, gid("edt:RefModuleRevision"))],
         [("Awarded", "Awarded", "Сдан", 1, gid("edt:LearningFlag"))])
register("TrackAward", "Path trophy", "Трофей пути",
         "Трофей за конкретную версию пути.",
         [("Learner", "Learner", "Учащийся", 1, gid("edt:RefLearner")),
          ("TrackRevision", "Path revision", "Версия пути", 2, gid("edt:RefTrackRevision"))],
         [("Awarded", "Awarded", "Закрыт", 1, gid("edt:LearningFlag"))])
register("Enrollment", "Enrollment", "Запись на путь",
         "Запись на карточку пути, не на редакцию текста.",
         [("Learner", "Learner", "Учащийся", 1, gid("edt:RefLearner")),
          ("Track", "Path", "Путь", 2, gid("edt:RefTrack"))],
         [("Source", "Source", "Источник", 1, gid("edt:EnrollmentSource")),
          ("DueAt", "Due", "Срок", 2, gid("edt:LearningDateTime"))])

# --- documents ---
def seq(name):
    dump(f"NumberSequences/{name}.json", {
        "kind": "NumberSequence",
        "object": {
            "padLength": 0, "startValue": 1000, "increment": 1, "resetPolicy": "None",
            "metaId": gid("seq:" + name), "name": name, "modelId": MODEL,
        },
    })

seq("ExamAttemptSeq")
seq("CertificateSeq")

lines_id = gid("tp:ExamAttemptLines")
dump("TableParts/ExamAttemptLines.json", {
    "kind": "TablePartType",
    "object": {
        "caption": cap("Attempt lines", "Строки попытки"),
        "tableName": "TP_ExamAttemptLines",
        "className": "ExamAttemptLinesTablePartRow",
        "softDeletion": False, "isLogged": False,
        "metaId": lines_id, "name": "ExamAttemptLines", "modelId": MODEL,
    },
    "properties": [
        {"tablePartTypeMetaId": lines_id, "fieldName": "Question", "name": "Question",
         "caption": cap("Question", "Вопрос"), "edtMetaId": gid("edt:RefExamQuestion"),
         "isRequired": False, "displayOrder": 1, "isVisible": True,
         "metaId": gid("tp:Question"), "modelId": MODEL},
        {"tablePartTypeMetaId": lines_id, "fieldName": "SortOrder", "name": "SortOrder",
         "caption": cap("Order", "Порядок"), "baseType": "Integer",
         "displayOrder": 2, "isVisible": True,
         "metaId": gid("tp:SortOrder"), "modelId": MODEL},
        {"tablePartTypeMetaId": lines_id, "fieldName": "PromptText", "name": "PromptText",
         "caption": cap("Question text", "Текст вопроса"), "baseType": "LargeText",
         "displayOrder": 3, "isVisible": True,
         "metaId": gid("tp:PromptText"), "modelId": MODEL},
        {"tablePartTypeMetaId": lines_id, "fieldName": "ChosenText", "name": "ChosenText",
         "caption": cap("Chosen answer", "Выбранный ответ"), "baseType": "LargeText",
         "displayOrder": 4, "isVisible": True,
         "metaId": gid("tp:ChosenText"), "modelId": MODEL},
        {"tablePartTypeMetaId": lines_id, "fieldName": "ChosenIsCorrect", "name": "ChosenIsCorrect",
         "caption": cap("Chosen option was correct", "Выбран верный"), "baseType": "Boolean",
         "displayOrder": 5, "isVisible": True,
         "metaId": gid("tp:ChosenIsCorrect"), "modelId": MODEL},
    ],
})

def doc_field(owner, name, en, ru, order, **extra):
    body = {
        "documentTypeMetaId": owner, "fieldName": name, "name": name,
        "caption": cap(en, ru),
        "isRequired": extra.pop("isRequired", False),
        "displayOrder": order, "isVisible": True,
        "isIndexed": extra.pop("isIndexed", False),
        "metaId": gid(f"hf:{owner}:{name}"), "modelId": MODEL,
    }
    body.update(extra)
    return body

def subtype(owner, name, en, ru, order, initial=False):
    return {
        "documentTypeMetaId": owner, "name": name, "subtypeValue": name,
        "subtypeCaption": cap(en, ru), "displayOrder": order, "isInitial": initial,
        "metaId": gid(f"st:{owner}:{name}"), "modelId": MODEL,
    }

attempt = gid("doc:ExamAttempt")
dump("Documents/ExamAttempt/ExamAttempt.object.json", {
    "kind": "Document",
    "object": {
        "caption": cap("Exam attempt", "Попытка экзамена"),
        "description": "Лист фиксируется при старте. Снимок текста остаётся после новой версии экзамена.",
        "requiresPosting": True, "postOnSave": True, "isCloneable": False,
        "numberSequenceMetaId": gid("seq:ExamAttemptSeq"),
        "documentGuid": "00000000-0000-0000-0000-000000000000",
        "metaId": attempt, "name": "ExamAttempt", "modelId": MODEL,
    },
    "headerFields": [
        doc_field(attempt, "Learner", "Learner", "Учащийся", 1, edtMetaId=gid("edt:RefLearner"), isRequired=True, isIndexed=True),
        doc_field(attempt, "ExamRevision", "Exam revision", "Версия экзамена", 2, edtMetaId=gid("edt:RefExamRevision"), isRequired=True, isIndexed=True),
        doc_field(attempt, "Score", "Score", "Результат, %", 3, baseType="Decimal", precision=9, scale=2),
    ],
    "subtypes": [
        subtype(attempt, "InProgress", "In progress", "Идёт", 1, True),
        subtype(attempt, "Passed", "Passed", "Сдан", 2),
        subtype(attempt, "Failed", "Failed", "Не сдан", 3),
    ],
    "tableParts": [{
        "documentTypeMetaId": attempt, "tablePartTypeMetaId": lines_id,
        "name": "Lines", "isCloneable": False,
        "metaId": gid("tpbind:Attempt"), "modelId": MODEL,
    }],
})

cert = gid("doc:Certificate")
dump("Documents/Certificate/Certificate.object.json", {
    "kind": "Document",
    "object": {
        "caption": cap("Certificate", "Сертификат"),
        "description": "Выдаётся за сданную попытку конкретной версии экзамена.",
        "requiresPosting": True, "postOnSave": True, "isCloneable": False,
        "numberSequenceMetaId": gid("seq:CertificateSeq"),
        "documentGuid": "00000000-0000-0000-0000-000000000000",
        "metaId": cert, "name": "Certificate", "modelId": MODEL,
    },
    "headerFields": [
        doc_field(cert, "Learner", "Learner", "Учащийся", 1, edtMetaId=gid("edt:RefLearner"), isRequired=True, isIndexed=True),
        doc_field(cert, "ExamRevision", "Exam revision", "Версия экзамена", 2, edtMetaId=gid("edt:RefExamRevision"), isRequired=True, isIndexed=True),
        doc_field(cert, "ExamAttempt", "Attempt", "Попытка", 3, baseType="Guid", isIndexed=True),
        doc_field(cert, "ExpiresAt", "Expires", "Истекает", 4, baseType="DateTime"),
    ],
    "subtypes": [
        subtype(cert, "Issued", "Issued", "Выдан", 1, True),
        subtype(cert, "Expired", "Expired", "Просрочен", 2),
    ],
    "tableParts": [],
})

# --- menu ---
root_id = gid("menu:root")
dict_id = gid("menu:dict")
doc_id = gid("menu:doc")
reg_id = gid("menu:reg")

def group(mid, name, en, ru, parent, order):
    item = {
        "caption": cap(en, ru), "displayOrder": order, "targetType": "Group",
        "metaId": mid, "name": name, "modelId": MODEL,
    }
    if parent:
        item["parentMetaId"] = parent
    return item

def leaf(name, en, ru, parent, order, target_type, target):
    return {
        "caption": cap(en, ru), "parentMetaId": parent, "displayOrder": order,
        "targetType": target_type, "targetMetaId": target,
        "metaId": gid("menu:" + name), "name": name, "modelId": MODEL,
    }

items = [
    group(root_id, "Learning", "Learning", "Обучение", None, 12),
    group(dict_id, "Dictionaries", "Dictionaries", "Справочники", root_id, 1),
    group(doc_id, "Documents", "Documents", "Документы", root_id, 2),
    group(reg_id, "Registers", "Totals", "Итоги", root_id, 3),
]
menu_dicts = [
    ("Learner", "Learners", "Учащиеся", "Learner"),
    ("Organization", "Organizations", "Организации", "Organization"),
    ("Track", "Paths", "Пути", "Track"),
    ("Module", "Modules", "Модули", "Module"),
    ("Unit", "Pages", "Страницы", "Unit"),
    ("Exam", "Exams", "Экзамены", "Exam"),
    ("ExamQuestion", "Exam questions", "Вопросы экзамена", "ExamQuestion"),
]
for i, (name, en, ru, target) in enumerate(menu_dicts, 1):
    items.append(leaf(name, en, ru, dict_id, i, "Dictionary", gid("dict:" + target)))
items.append(leaf("ExamAttempt", "Exam attempts", "Попытки", doc_id, 1, "Document", attempt))
items.append(leaf("Certificate", "Certificates", "Сертификаты", doc_id, 2, "Document", cert))
for i, (name, en, ru) in enumerate([
    ("LearningEvent", "Learning history", "История обучения"),
    ("ModuleAward", "Module badges", "Значки модулей"),
    ("TrackAward", "Path trophies", "Трофеи путей"),
    ("Enrollment", "Enrollments", "Записи на путь"),
], 1):
    items.append(leaf(name, en, ru, reg_id, i, "Register", gid("reg:" + name)))
dump("Menu/menu.json", {"kind": "Menu", "items": items})

# --- constant ---
group_id = gid("constgroup")
dump("Constants/_groups.json", {
    "kind": "ConstantGroups",
    "items": [{
        "caption": cap("Learning", "Обучение"),
        "metaId": group_id, "name": "Learning", "modelId": MODEL,
    }],
})
dump("Constants/DefaultCertificateDays.json", {
    "kind": "GlobalConstant",
    "object": {
        "caption": cap("Certificate validity, days", "Срок сертификата, дни"),
        "groupMetaId": group_id,
        "constantType": "Integer", "numberValue": 365, "value": 365, "displayValue": "365",
        "metaId": gid("const:days"), "name": "DefaultCertificateDays", "modelId": MODEL,
    },
})

# --- services, job, test envelopes (code written separately) ---
def service(name, description):
    sid = gid("svc:" + name)
    script = gid("script:" + name)
    dump(f"Services/{name}/{name}.script.json", {
        "kind": "Script",
        "object": {
            "scriptType": "Service", "objectType": "Service", "objectName": name,
            "executionOrder": 0, "metaId": script, "name": name, "modelId": MODEL,
        },
    })
    dump(f"Services/{name}/{name}.json", {
        "kind": "Service",
        "object": {
            "description": description, "namespace": "ZuloOne.Services",
            "scriptMetaId": script, "metaId": sid, "name": name, "modelId": MODEL,
        },
    })

service("LearningCatalog", "Публикует версию модуля и страницы. Повтор того же текста новую версию не создаёт.")
service("LearningProgress", "Пишет событие обучения по версии страницы.")
service("KnowledgeCheck", "Сверяет ответ проверки с ключом версии и ставит значок модуля.")
service("ExamSession", "Публикует версию экзамена, начинает попытку со снимком листа, выдаёт и просрочивает сертификат.")

dump("Documents/ExamAttempt/Events/ExamAttemptEventHandler.script.json", {
    "kind": "Script",
    "object": {
        "scriptType": "EventHandler", "objectType": "Document",
        "objectName": "ExamAttempt", "objectMetaId": attempt,
        "executionOrder": 0,
        "metaId": gid("script:ExamAttemptEventHandler"),
        "name": "ExamAttemptEventHandler", "modelId": MODEL,
    },
})

job = gid("job:ExpireCertificates")
dump("Jobs/ExpireCertificates/ExpireCertificatesTask.script.json", {
    "kind": "Script",
    "object": {
        "scriptType": "Task", "objectType": "Job", "objectName": "ExpireCertificates",
        "objectMetaId": job, "executionOrder": 0,
        "metaId": gid("script:ExpireCertificatesTask"),
        "name": "ExpireCertificatesTask", "modelId": MODEL,
    },
})
dump("Jobs/ExpireCertificates/ExpireCertificates.json", {
    "kind": "Job",
    "object": {
        "description": "Переводит выданные сертификаты с прошедшим сроком в просроченные.",
        "scriptMetaId": gid("script:ExpireCertificatesTask"),
        "cronExpression": "20 3 * * *", "isActive": True, "executeSingle": True,
        "queueTtlMinutes": 60, "metaId": job, "name": "ExpireCertificates", "modelId": MODEL,
    },
})

dump("Tests/LearningPublishTest.json", {
    "kind": "Test",
    "object": {
        "description": "Вторая публикация страницы и экзамена не затирает первую версию и снимок попытки.",
        "isActive": True, "groupName": "Бизнес-слой.Сервисы",
        "metaId": gid("test:LearningPublish"), "name": "LearningPublishTest", "modelId": MODEL,
    },
})

print("MODEL", MODEL)
print("files", len(list(root.rglob('*.json'))))
