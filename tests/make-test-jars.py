import zipfile, json, os
base = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_jartest")
os.makedirs(base, exist_ok=True)

# 1. Fabric
fabric = {
  "schemaVersion": 1,
  "id": "examplemod",
  "version": "1.2.3",
  "name": "Example Mod",
  "depends": {
    "fabricloader": ">=0.14.0",
    "minecraft": "1.20.x",
    "fabric-api": "*",
    "cloth-config": ">=11.0.0"
  }
}
with zipfile.ZipFile(os.path.join(base, "fabric.jar"), "w") as z:
    z.writestr("fabric.mod.json", json.dumps(fabric, indent=2))

# 2. Forge (mods.toml)
toml = '''modLoader="javafml"
loaderVersion="[47,)"
license="MIT"

[[mods]]
modId="forgemod"
version="2.0.0"
displayName="Forge Mod"

[[dependencies.forgemod]]
    modId="forge"
    mandatory=true
[[dependencies.forgemod]]
    modId="minecraft"
    mandatory=true
[[dependencies.forgemod]]
    modId="jei"
    mandatory=true
'''
with zipfile.ZipFile(os.path.join(base, "forge.jar"), "w") as z:
    z.writestr("META-INF/mods.toml", toml)

# 3. Legacy Forge (mcmod.info)
legacy = [{"modid": "oldmod", "name": "Old Mod", "version": "1.0", "mcversion": "1.12.2"}]
with zipfile.ZipFile(os.path.join(base, "legacy.jar"), "w") as z:
    z.writestr("mcmod.info", json.dumps(legacy))

# 4. 无声明（普通库 JAR）
with zipfile.ZipFile(os.path.join(base, "plain.jar"), "w") as z:
    z.writestr("some/Class.class", b"\xca\xfe\xba\xbe")

print("created 4 jars")