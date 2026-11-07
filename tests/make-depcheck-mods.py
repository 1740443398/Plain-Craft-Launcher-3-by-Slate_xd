import zipfile, json, os
base = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_depmods", "mods")
os.makedirs(base, exist_ok=True)

# A：需要 cloth-config >=11 和 fabric-api（会被跳过）
a = {"schemaVersion":1,"id":"moda","version":"1.0.0","name":"Mod A",
     "depends":{"fabricloader":">=0.14.0","minecraft":"1.20.x","fabric-api":"*","cloth-config":">=11.0.0"}}
with zipfile.ZipFile(os.path.join(base,"moda.jar"),"w") as z:
    z.writestr("fabric.mod.json", json.dumps(a))

# B：cloth-config 实际版本 9.0.0（低于要求的 11）
b = {"schemaVersion":1,"id":"cloth-config","version":"9.0.0","name":"Cloth Config"}
with zipfile.ZipFile(os.path.join(base,"cloth.jar"),"w") as z:
    z.writestr("fabric.mod.json", json.dumps(b))

# C：需要一个完全没装的 mod
c = {"schemaVersion":1,"id":"modc","version":"2.0.0","name":"Mod C",
     "depends":{"fabricloader":">=0.14.0","sodium":">=0.5.0"}}
with zipfile.ZipFile(os.path.join(base,"modc.jar"),"w") as z:
    z.writestr("fabric.mod.json", json.dumps(c))

print("created 3 mods")