"""Read-only validation of shipped stickers, source hashes and interaction references."""
import hashlib
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1] / "content" / "wechat"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def resolve(relative):
    path = (ROOT / relative).resolve()
    require(path.is_relative_to(ROOT.resolve()) and path.is_file(), f"Missing or unsafe path: {relative}")
    return path


def main():
    assets = [asset for path in sorted(ROOT.glob("catalog*.json")) for asset in read(path)["assets"]]
    by_id = {asset["id"]: asset for asset in assets}
    require(len(by_id) == len(assets) > 0, "Duplicate or empty asset catalog")
    frames = pages = 0
    for asset in assets:
        ident, width, height = asset["id"], asset["width"], asset["height"]
        count = asset["frameCount"]
        require(width > 0 and height > 0 and count > 0, f"Invalid dimensions: {ident}")
        require(len(asset["durationsMs"]) == count and min(asset["durationsMs"]) >= 20, f"Invalid timing: {ident}")
        require(asset["role"] in {"yier", "bubu", "pair", "scene", "unreviewed"}, f"Invalid role: {ident}")
        original = resolve(asset["original"])
        require(hashlib.sha256(original.read_bytes()).hexdigest() == asset["sha256"], f"Source hash mismatch: {ident}")
        with Image.open(resolve(asset["poster"])) as image:
            image.verify()
        next_frame = 0
        for page in asset["pages"]:
            require(page["firstFrame"] == next_frame and page["frameCount"] > 0 and page["columns"] > 0, f"Non-contiguous atlas: {ident}")
            with Image.open(resolve(page["file"])) as image:
                rows = (page["frameCount"] + page["columns"] - 1) // page["columns"]
                require(image.width >= page["columns"] * width and image.height >= rows * height, f"Atlas bounds: {ident}")
                image.verify()
            next_frame += page["frameCount"]
            pages += 1
        require(next_frame == count, f"Missing frames: {ident}")
        frames += count

    rules, states = [], {}
    for path in sorted(ROOT.glob("interactions*.json")):
        document = read(path)
        rules.extend(document["interactions"])
        for role, mapping in document.get("states", {}).items():
            states.setdefault(role, {}).update(mapping)
    require(len({rule["id"] for rule in rules}) == len(rules), "Duplicate interaction IDs")
    for rule in rules:
        ident, ids = rule["id"], rule["assetIds"]
        require(ids and all(key in by_id for key in ids), f"Broken reference: {ident}")
        policy = rule.get("positionRequirement", "")
        require(policy in {"", "any", "yier-left", "bubu-left", "chase"}, f"Invalid position: {ident}")
        if policy in {"yier-left", "bubu-left"}:
            require(rule["leftCharacter"] == policy.split("-")[0], f"Conflicting position: {ident}")
        if policy == "chase":
            require(rule["mode"] == "chase", f"Invalid chase: {ident}")
        if rule["mode"] in {"simultaneous", "chase"}:
            require(len(ids) == 2 and [by_id[key]["role"] for key in ids] == ["yier", "bubu"], f"Invalid linked pair: {ident}")
            require(rule["triggerIds"] and set(rule["triggerIds"]) <= set(ids), f"Invalid trigger: {ident}")
            require(0 < rule["durationSeconds"] <= 60, f"Invalid duration: {ident}")
            for key in ("mirrorWhenMovingLeft", "mirrorWhenMovingRight"):
                require(set(rule.get(key, [])) <= set(ids), f"Invalid mirror: {ident}")
    for role, mapping in states.items():
        require(all(key in by_id and by_id[key]["role"] == role for key in mapping.values()), f"Invalid state mapping: {role}")
    semantics = [item for path in sorted(ROOT.glob("semantics*.json")) for item in read(path)["entries"]]
    require(len(semantics) == len(by_id) and {item["id"] for item in semantics} == set(by_id), "Semantic coverage mismatch")
    for item in semantics:
        require(all(item.get(key, "").strip() for key in ("observedAction", "partnerPossibility", "positionNote")), f"Missing description: {item['id']}")
        for match in item.get("matches", []):
            rule = next((rule for rule in rules if rule["id"] == match["interactionId"]), None)
            require(rule is not None and item["id"] in rule["assetIds"] and match["assetId"] in rule["assetIds"], f"Broken semantic match: {item['id']}")
    print(json.dumps({"assets": len(assets), "sourceHashes": len(assets), "frames": frames, "atlasPages": pages,
                      "interactions": len(rules), "semanticEntries": len(semantics), "status": "passed"}))


if __name__ == "__main__":
    main()
